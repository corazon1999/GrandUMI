#!/usr/bin/env python3
"""仅给测试服已有管理员发放六种 S1 称号；重复执行不会重复授予。"""

import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import subprocess
import uuid


DATA_DIR = Path('/data/grandumi-test')
POLICY_FILE = Path('/opt/grandumi-test/服务端WebSocket/GlobalAnnouncementPolicy.cs')
TITLES = ('S1 海贼王', 'S1 四皇', 'S1 海军元帅', 'S1 海军大将', 'S1 世界之王', 'S1 五老星')


def unchanged_data_digest(connection):
    """事务内核对其他表，保证不改排名、钱包、装备和真实赛季荣誉。"""
    digest = hashlib.sha256()
    tables = connection.execute(
        "SELECT name FROM sqlite_master WHERE type='table' "
        "AND name NOT LIKE 'sqlite_%' AND name!='rank_admin_test_honors' ORDER BY name"
    ).fetchall()
    for (name,) in tables:
        digest.update(name.encode('utf-8'))
        quoted = '"' + name.replace('"', '""') + '"'
        rows = sorted(json.dumps(row, ensure_ascii=False) for row in connection.execute('SELECT * FROM ' + quoted))
        for row in rows:
            digest.update(row.encode('utf-8'))
            digest.update(b'\n')
    return digest.hexdigest()


def main():
    # 不接受环境名或路径参数，拒绝重定向到其他环境的数据目录。
    if DATA_DIR.resolve() != DATA_DIR or DATA_DIR.is_symlink():
        raise RuntimeError('测试服数据目录存在路径重定向，停止发放。')
    service = subprocess.run(
        ['systemctl', 'show', 'grandumi-test-backend.service', '--property=Environment', '--value'],
        check=True, capture_output=True, text=True,
    ).stdout
    if 'GRANDUMI_DATA_DIR=/data/grandumi-test' not in service.split() or 'GRANDUMI_TEST_SEASON_HONORS=1' not in service.split():
        raise RuntimeError('测试服未启用测试称号开关，请先部署已验证的代码。')
    policy = POLICY_FILE.read_text(encoding='utf-8')
    block = re.search(r'AuthorizedAccounts\s*=.*?\{(.*?)\};', policy, re.S)
    admins = re.findall(r'"([^"\r\n]+)"', block.group(1)) if block else []
    if not admins or len(admins) != len(set(admins)):
        raise RuntimeError('无法可靠读取管理员白名单，停止发放。')

    player_path = DATA_DIR / 'players.db'
    if not player_path.is_file() or player_path.is_symlink() or player_path.resolve().parent != DATA_DIR:
        raise RuntimeError('测试服玩家资料库缺失或存在路径重定向。')
    with sqlite3.connect('file:' + str(player_path) + '?mode=ro', uri=True) as players:
        for account in admins:
            if players.execute('SELECT 1 FROM players WHERE account=?', (account,)).fetchone() is None:
                raise RuntimeError('测试服管理员缺少现有玩家资料：' + account)

    paths = [DATA_DIR / 'ranked.db', DATA_DIR / 'ranked-wild.db']
    for path in paths:
        if not path.is_file() or path.is_symlink() or path.resolve().parent != DATA_DIR:
            raise RuntimeError('测试排位数据库缺失或存在路径重定向：' + str(path))
        with sqlite3.connect('file:' + str(path) + '?mode=ro', uri=True) as connection:
            if connection.execute("SELECT 1 FROM sqlite_master WHERE name='rank_admin_test_honors' AND type='table'").fetchone() is None:
                raise RuntimeError('测试排位数据库尚未完成称号升级：' + str(path))

    now = datetime.datetime.now(datetime.timezone.utc)
    stamp = now.isoformat().replace('+00:00', 'Z')
    backup_dir = DATA_DIR / 'season-title-test-backups' / (now.strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:8])
    if backup_dir.resolve().is_relative_to(DATA_DIR) is False:
        raise RuntimeError('测试备份目录超出测试数据范围。')
    backup_dir.mkdir(parents=True, mode=0o700)

    for path in paths:
        backup = backup_dir / path.name
        descriptor = os.open(backup, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        os.close(descriptor)
        with sqlite3.connect('file:' + str(path) + '?mode=ro', uri=True) as source:
            with sqlite3.connect(backup) as destination:
                source.backup(destination)

        with sqlite3.connect(path, timeout=15) as connection:
            connection.execute('BEGIN IMMEDIATE')
            before = unchanged_data_digest(connection)
            for account in admins:
                key = hashlib.sha256(account.strip().upper().encode('utf-8')).hexdigest()
                connection.executemany(
                    'INSERT OR IGNORE INTO rank_admin_test_honors(season_id,account_key,title,granted_at_utc) VALUES(?,?,?,?)',
                    [('S1', key, title, stamp) for title in TITLES],
                )
                actual = {row[0] for row in connection.execute('SELECT title FROM rank_admin_test_honors WHERE season_id=? AND account_key=?', ('S1', key))}
                if actual != set(TITLES):
                    raise RuntimeError('管理员测试称号核验失败：' + account)
            if unchanged_data_digest(connection) != before:
                raise RuntimeError('发放影响了其他排位数据，事务将回滚。')
            connection.commit()
        print(json.dumps({'模式': '标准' if path.name == 'ranked.db' else '狂野', '管理员': admins, '称号': TITLES, '备份': str(backup), '其他数据': '保持原样'}, ensure_ascii=False))


if __name__ == '__main__':
    main()
