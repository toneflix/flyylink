import { LoaderCircle } from "lucide-react";
import type { CSSProperties } from "react";

/** A spinning ring, for a button that is waiting on the server. */
export function Spinner({ size = 18 }: { size?: number }) {
  return <LoaderCircle className="spinner" size={size} aria-hidden />;
}

/** A shimmering placeholder in the shape of the text or control that is still loading. */
function Bone({ width = "100%", height = 16, round = false }: { width?: CSSProperties["width"]; height?: number; round?: boolean }) {
  return <span className="bone" style={{ width, height, borderRadius: round ? 999 : 8 }} />;
}

/** The buy panel's shape while the plans and prices load. */
export function BuySkeleton() {
  return (
    <div className="licence-panel" role="status" aria-label="Loading the plans">
      <Bone height={44} round />
      <Bone width="45%" height={40} />
      {[70, 60, 75].map((width) => (
        <Bone key={width} width={`${width}%`} />
      ))}
      <Bone height={52} round />
    </div>
  );
}

/** The account panel's shape while the account loads. */
export function AccountSkeleton() {
  return (
    <div className="licence-panel" role="status" aria-label="Loading your account">
      <div className="account-head">
        <div className="bones">
          <Bone width={220} height={26} />
          <Bone width={160} />
        </div>
        <Bone width={110} height={46} round />
      </div>
      <Bone width={140} height={20} />
      {[0, 1].map((row) => (
        <div key={row} className="bone-row">
          <Bone width={36} height={36} />
          <div className="bones">
            <Bone width="50%" />
            <Bone width="35%" height={12} />
          </div>
        </div>
      ))}
    </div>
  );
}
