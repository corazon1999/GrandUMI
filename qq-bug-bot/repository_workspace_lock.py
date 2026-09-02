# -*- coding: utf-8 -*-
"""统一验证与本机管理员工作器共享的仓库互斥锁。"""

from __future__ import annotations

import ctypes
import hashlib
import os
from pathlib import Path


class RepositoryLockError(RuntimeError):
    """仓库锁目录或操作系统锁句柄不可用。"""


def _normalized_repository(repository_root: str | Path) -> str:
    value = str(Path(repository_root).resolve()).rstrip("\\/")
    return os.path.normcase(value)


def repository_lock_path(
    repository_root: str | Path,
    lock_root: str | Path | None = None,
) -> Path:
    """返回跨 PowerShell/Python 稳定一致的锁文件路径。"""
    normalized = _normalized_repository(repository_root)
    digest = hashlib.sha256(normalized.encode("utf-8")).hexdigest()
    configured_root = str(
        lock_root
        or os.environ.get("GRANDUMI_WORKSPACE_LOCK_ROOT")
        or ("E:/GrandUMI-Temp/Locks" if os.name == "nt" else "/tmp/grandumi-locks")
    )
    root = Path(configured_root).resolve()
    if os.name == "nt" and root.drive.upper() != "E:":
        raise RepositoryLockError(
            f"Windows 仓库互斥锁必须位于 E 盘，实际为：{root}"
        )
    root.mkdir(parents=True, exist_ok=True)
    return root / f"repository-{digest}.lock"


class RepositoryWorkspaceLock:
    """持有进程退出时由操作系统自动释放的排他文件句柄。"""

    _ERROR_SHARING_VIOLATION = 32
    _ERROR_LOCK_VIOLATION = 33
    _INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

    def __init__(
        self,
        repository_root: str | Path,
        lock_root: str | Path | None = None,
    ) -> None:
        self.path = repository_lock_path(repository_root, lock_root)
        self.acquired = False
        self._handle = None
        self._file = None

    def try_acquire(self) -> bool:
        if self.acquired:
            raise RepositoryLockError("同一个锁实例不能重复获取。")
        if os.name == "nt":
            kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
            create_file = kernel32.CreateFileW
            create_file.argtypes = [
                ctypes.c_wchar_p,
                ctypes.c_uint32,
                ctypes.c_uint32,
                ctypes.c_void_p,
                ctypes.c_uint32,
                ctypes.c_uint32,
                ctypes.c_void_p,
            ]
            create_file.restype = ctypes.c_void_p
            handle = create_file(
                str(self.path),
                0x80000000 | 0x40000000,  # GENERIC_READ | GENERIC_WRITE
                0,  # 禁止其他进程共享读、写或删除
                None,
                4,  # OPEN_ALWAYS
                0x80,  # FILE_ATTRIBUTE_NORMAL
                None,
            )
            if handle == self._INVALID_HANDLE_VALUE:
                error = ctypes.get_last_error()
                if error in (
                    self._ERROR_SHARING_VIOLATION,
                    self._ERROR_LOCK_VIOLATION,
                ):
                    return False
                raise OSError(error, os.strerror(error), str(self.path))
            self._handle = handle
            self.acquired = True
            return True

        import fcntl

        file = self.path.open("a+b")
        try:
            fcntl.flock(file.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            file.close()
            return False
        self._file = file
        self.acquired = True
        return True

    def release(self) -> None:
        if not self.acquired:
            return
        if os.name == "nt":
            kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
            if not kernel32.CloseHandle(ctypes.c_void_p(self._handle)):
                error = ctypes.get_last_error()
                raise OSError(error, os.strerror(error), str(self.path))
            self._handle = None
        else:
            import fcntl

            fcntl.flock(self._file.fileno(), fcntl.LOCK_UN)
            self._file.close()
            self._file = None
        self.acquired = False

    def __enter__(self) -> "RepositoryWorkspaceLock":
        self.try_acquire()
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        self.release()
