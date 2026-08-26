export default function TurnExtensionIcon({ className = "h-4 w-4" }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <circle cx="10.5" cy="12" r="7" />
      <path d="M10.5 8v4.5l2.7 1.6M8.5 2.5h4" />
      <path d="M18.5 14.5v6M15.5 17.5h6" />
    </svg>
  );
}
