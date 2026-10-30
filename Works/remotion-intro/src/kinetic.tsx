import React from "react";
import { spring, useCurrentFrame, useVideoConfig } from "remotion";
import { C } from "./theme";

/**
 * 逐字上浮 + 去模糊的动效标题，用于把大标题压在拍点上。
 * delay 一般取 15 的整数倍（= 拍点）。
 */
export const KineticText: React.FC<{
  text: string;
  delay?: number;
  stagger?: number;
  accentFrom?: number;
  style?: React.CSSProperties;
}> = ({ text, delay = 0, stagger = 1, accentFrom, style }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const chars = Array.from(text);
  return (
    <div style={{ display: "flex", flexWrap: "wrap", justifyContent: "center", ...style }}>
      {chars.map((ch, i) => {
        const p = spring({
          frame: Math.max(0, frame - delay - i * stagger),
          fps,
          config: { damping: 200 },
          durationInFrames: 16,
        });
        const accent = accentFrom !== undefined && i >= accentFrom;
        return (
          <span
            key={i}
            style={{
              display: "inline-block",
              whiteSpace: "pre",
              color: accent ? C.accent : undefined,
              opacity: p,
              transform: `translateY(${(1 - p) * 26}px)`,
              filter: p < 1 ? `blur(${(1 - p) * 5}px)` : undefined,
            }}
          >
            {ch}
          </span>
        );
      })}
    </div>
  );
};
