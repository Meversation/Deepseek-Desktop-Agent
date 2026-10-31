import React from "react";
import { AbsoluteFill, Img, staticFile, useVideoConfig } from "remotion";
import { C, FONT, MONO } from "./theme";

/** 周报封面 · 16:9 / 4:3 自适应，使用真实运行截图 */
export const WeeklyCover: React.FC = () => {
  const { width, height } = useVideoConfig();
  const u = width / 1920;
  const textW = width * 0.36;
  const shotW = width * 0.53;
  const shotH = shotW * (728 / 1366);
  const shotX = width - shotW - width * 0.035;
  const shotY = (height - shotH) / 2 + height * 0.04;
  const titleSize = Math.round(150 * u);
  const marginX = Math.round(108 * u);

  return (
    <AbsoluteFill style={{ backgroundColor: "#06080D", fontFamily: FONT }}>
      <Img
        src={staticFile("report/app-bg.png")}
        style={{ width: "100%", height: "100%", objectFit: "cover", opacity: 0.45 }}
      />
      <AbsoluteFill style={{ background: "radial-gradient(900px 640px at 10% 0%, rgba(35,62,120,0.55), transparent 65%)" }} />
      <AbsoluteFill style={{ background: "linear-gradient(180deg, rgba(6,8,13,0.35) 0%, rgba(6,8,13,0.96) 100%)" }} />

      <div
        style={{
          position: "absolute",
          right: -24 * u,
          bottom: -170 * u,
          fontSize: 520 * u,
          fontWeight: 800,
          letterSpacing: -22 * u,
          lineHeight: 1,
          color: "rgba(255,255,255,0.04)",
        }}
      >
        W44
      </div>

      <div style={{ position: "absolute", left: marginX, top: height * 0.235, width: textW }}>
        <div style={{ fontFamily: MONO, fontSize: 19 * u, color: C.accent, letterSpacing: 5 * u }}>
          REAL TASK DEMO · 2026 W44
        </div>
        <div
          style={{
            marginTop: 18 * u,
            fontSize: titleSize,
            fontWeight: 800,
            letterSpacing: -5 * u,
            lineHeight: 1,
            color: C.text,
          }}
        >
          桌面 Agent 跑通
        </div>
        <div
          style={{
            marginTop: 8 * u,
            fontSize: titleSize * 0.85,
            fontWeight: 800,
            letterSpacing: -4 * u,
            lineHeight: 1.1,
            color: C.accent,
          }}
        >
          一个真实任务
        </div>
        <div style={{ marginTop: 20 * u, fontSize: 34 * u, color: "#B7C1D6", lineHeight: 1.4 }}>
          0成本 · 非反代 · 直接操作本地文件
          <br />
          附完整复现步骤
        </div>
        <div style={{ marginTop: 26 * u, display: "flex", flexWrap: "wrap", gap: 12 * u }}>
          {["7 次提交", "9 项修复", "v1.5.1"].map((t) => (
            <span
              key={t}
              style={{
                padding: `${9 * u}px ${16 * u}px`,
                borderRadius: 999,
                border: `1px solid ${C.border}`,
                background: "rgba(255,255,255,0.05)",
                fontSize: 20 * u,
                color: C.text,
              }}
            >
              {t}
            </span>
          ))}
        </div>
        <div style={{ marginTop: 22 * u, fontFamily: MONO, fontSize: 17 * u, color: C.subtle, letterSpacing: 1 }}>
          10.25 — 10.31 · 全部真实运行界面
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          left: shotX,
          top: shotY,
          width: shotW,
          height: shotH,
          borderRadius: 16 * u,
          overflow: "hidden",
          border: `1px solid ${C.border}`,
          boxShadow: "0 30px 90px rgba(0,0,0,0.7)",
        }}
      >
        <Img src={staticFile("report/app.png")} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
        <div
          style={{
            position: "absolute",
            left: "24%",
            top: "6%",
            width: "74%",
            height: "70%",
            border: `${2 * u}px solid ${C.accent}`,
            borderRadius: 8 * u,
            background: "rgba(77,141,255,0.10)",
            boxShadow: `0 0 ${24 * u}px rgba(77,141,255,0.55)`,
          }}
        />
        <div
          style={{
            position: "absolute",
            right: 16 * u,
            bottom: 14 * u,
            padding: `${7 * u}px ${14 * u}px`,
            borderRadius: 999,
            background: "rgba(6,8,13,0.84)",
            border: `1px solid ${C.accent2}`,
            fontSize: 15 * u,
            color: C.text,
          }}
        >
          真实界面 · v1.5.1
        </div>
      </div>
    </AbsoluteFill>
  );
};
