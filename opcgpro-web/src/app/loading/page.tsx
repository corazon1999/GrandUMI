export default function LoadingPage() {
  return (
    <div className="flex h-screen items-center justify-center bg-gray-950">
      <div className="text-center">
        <h1 className="text-4xl font-bold text-white mb-4">GrandUMI</h1>
        <div className="w-48 h-1 bg-gray-800 rounded-full overflow-hidden mx-auto">
          <div className="h-full bg-orange-500 rounded-full animate-pulse w-2/3" />
        </div>
        <p className="text-gray-400 mt-4 text-sm">加载中...</p>
      </div>
    </div>
  );
}
