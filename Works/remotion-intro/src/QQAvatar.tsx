import React from "react";
import { AbsoluteFill, Img, staticFile } from "remotion";
import { C, FONT } from "./theme";

/**
 * QQ 群头像：1000 × 1000，正方形。
 * 图标居中放大版，文字下置，圆形裁切后仍完整可读。
 */
export const QQAvatar: React.FC = () => {
  return (
    <AbsoluteFill style={{ backgroundColor: C.bg, fontFamily: FONT, overflow: "hidden" }}>
      <AbsoluteFill style={{ background: "radial-gradient(720px 720px at 50% 38%, #182A44 0%, #0B0F16 70%)" }} />
      <AbsoluteFill
        style={{
          backgroundImage:
            "linear-gradient(rgba(96,165,250,0.05) 1px, transparent 1px), linear-gradient(90deg, rgba(96,165,250,0.05) 1px, transparent 1px)",
          backgroundSize: "46px 46px",
          maskImage: "radial-gradient(circle at 50% 45%, black 25%, transparent 72%)",
          WebkitMaskImage: "radial-gradient(circle at 50% 45%, black 25%, transparent 72%)",
        }}
      />

      {/* 光环与轨道 */}
      <div
        style={{
          position: "absolute",
          left: 220,
          top: 130,
          width: 560,
          height: 560,
          borderRadius: "50%",
          background: "radial-gradient(circle, rgba(77,107,254,0.42) 0%, transparent 68%)",
        }}
      />
      <div
        style={{
          position: "absolute",
          left: 180,
          top: 110,
          width: 640,
          height: 640,
          borderRadius: "50%",
          border: "2px solid rgba(96,165,250,0.22)",
        }}
      />

      {/* 应用图标 */}
      <Img
        src={staticFile("icon.png")}
        style={{
          position: "absolute",
          left: 320,
          top: 210,
          width: 360,
          height: 360,
          borderRadius: 86,
          border: "2px solid rgba(96,165,250,0.45)",
          boxShadow: "0 36px 100px rgba(0,0,0,0.7), 0 0 90px rgba(77,107,254,0.4)",
        }}
      />

      {/* ¥0 角标 */}
      <div
        style={{
          position: "absolute",
          left: 636,
          top: 490,
          width: 124,
          height: 124,
          borderRadius: "50%",
          background: `linear-gradient(140deg, ${C.accent2}, ${C.accent})`,
          boxShadow: "0 16px 40px rgba(77,107,254,0.55)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          color: "#fff",
          fontSize: 54,
          fontWeight: 800,
          transform: "rotate(-8deg)",
        }}
      >
        ¥0
      </div>

      {/* 名称 */}
      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          top: 780,
          textAlign: "center",
          fontSize: 54,
          fontWeight: 800,
          letterSpacing: 10,
          color: C.text,
        }}
      >
        DEEPSEEK
      </div>
      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          top: 856,
          textAlign: "center",
          fontSize: 26,
          fontWeight: 700,
          letterSpacing: 12,
          color: C.subtle,
        }}
      >
        DESKTOP AGENT
      </div>

      <AbsoluteFill
        style={{ background: "radial-gradient(700px 700px at 50% 46%, transparent 50%, rgba(0,0,0,0.55) 100%)" }}
      />
    </AbsoluteFill>
  );
};
