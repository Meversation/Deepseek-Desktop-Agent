import React from "react";
import { AbsoluteFill, Img, staticFile, useVideoConfig } from "remotion";
import { C, FONT, MONO } from "./theme";

/**
 * B 站封面：同一套版式适配 16:10 / 4:3 / 16:9。
 * 字体和间距按宽度等比缩放，卡片按高度定位，保证任何比例下都不裁切。
 */

export const BilibiliCover: React.FC = () => {
  const { width: W, height: H } = useVideoConfig();
  const u = W / 1146; // 字号缩放基准

  const Chip: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <div
      style={{
        padding: `${7 * u}px ${14 * u}px`,
        borderRadius: 999,
        border: `1px solid ${C.border}`,
        background: "rgba(26,32,43,0.9)",
        color: C.text,
        fontSize: 18 * u,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {children}
    </div>
  );

  const Line: React.FC<{ who: string; text: string; color: string }> = ({ who, text, color }) => (
    <div style={{ display: "flex", gap: 8 * u, fontSize: 15 * u, lineHeight: 1.6, fontFamily: MONO }}>
      <span style={{ color: C.subtle, width: 78 * u, flexShrink: 0 }}>{who}</span>
      <span style={{ color }}>{text}</span>
    </div>
  );

  return (
    <AbsoluteFill style={{ backgroundColor: C.bg, fontFamily: FONT, overflow: "hidden" }}>
      <AbsoluteFill style={{ background: "radial-gradient(760px 520px at 18% 10%, #17243A 0%, #0B0F16 66%)" }} />
      <AbsoluteFill
        style={{
          backgroundImage:
            "linear-gradient(rgba(96,165,250,0.06) 1px, transparent 1px), linear-gradient(90deg, rgba(96,165,250,0.06) 1px, transparent 1px)",
          backgroundSize: `${44 * u}px ${44 * u}px`,
          maskImage: "radial-gradient(circle at 45% 40%, black 20%, transparent 80%)",
          WebkitMaskImage: "radial-gradient(circle at 45% 40%, black 20%, transparent 80%)",
        }}
      />
      <AbsoluteFill
        style={{ background: "radial-gradient(520px 420px at 82% 62%, rgba(77,107,254,0.32), transparent 70%)" }}
      />

      {/* 左：文案 */}
      <div style={{ position: "absolute", left: 0.054 * W, top: 0.09 * H, width: 0.5 * W }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12 * u }}>
          <Img src={staticFile("icon.png")} style={{ width: 34 * u, height: 34 * u, borderRadius: 8 * u }} />
          <span style={{ fontSize: 17 * u, letterSpacing: 3 * u, color: C.subtle, fontWeight: 700 }}>
            DEEPSEEK · DESKTOP AGENT
          </span>
        </div>

        <div
          style={{
            marginTop: 26 * u,
            fontSize: 50 * u,
            fontWeight: 800,
            color: C.text,
            lineHeight: 1.15,
            letterSpacing: -1 * u,
          }}
        >
          网页版 DeepSeek
        </div>
        <div
          style={{
            marginTop: 4 * u,
            fontSize: 72 * u,
            fontWeight: 800,
            lineHeight: 1.05,
            letterSpacing: -2 * u,
            background: `linear-gradient(100deg, ${C.accent} 0%, ${C.accent2} 70%)`,
            WebkitBackgroundClip: "text",
            WebkitTextFillColor: "transparent",
          }}
        >
          变成本地 Agent
        </div>

        <div style={{ marginTop: 20 * u, fontSize: 23 * u, color: C.text, fontWeight: 600 }}>
          自己会敲命令 · 工具在本机执行 · 0 成本
        </div>

        <div style={{ marginTop: 22 * u, display: "flex", gap: 12 * u, flexWrap: "wrap" }}>
          <Chip>¥0 调用</Chip>
          <Chip>无需 API Key</Chip>
          <Chip>Edge CDP 内嵌</Chip>
        </div>

        <div
          style={{
            marginTop: 40 * u,
            display: "flex",
            alignItems: "center",
            gap: 10 * u,
            color: C.subtle,
            fontSize: 17 * u,
          }}
        >
          <span style={{ color: C.accent, fontWeight: 800 }}>WinForms</span>
          <span>·</span>
          <span>.NET 10</span>
          <span>·</span>
          <span>单文件 104 MB 免安装</span>
        </div>

        <div style={{ marginTop: 62 * u }}>
          <div style={{ height: 1, background: `linear-gradient(90deg, ${C.border}, transparent)` }} />
          <div style={{ marginTop: 16 * u, fontSize: 19 * u, color: C.subtle, letterSpacing: 0.5 * u }}>
            对话 · 内嵌网页 · 工具调用 · 命令执行 · 结果回传
          </div>
        </div>
      </div>

      {/* 右：界面卡片 */}
      <div
        style={{
          position: "absolute",
          left: 0.564 * W,
          top: 0.178 * H,
          width: 0.391 * W,
          height: 0.46 * H,
          borderRadius: 14 * u,
          background: C.bg2,
          border: `1px solid ${C.border}`,
          boxShadow: "0 34px 90px rgba(0,0,0,0.65), 0 0 60px rgba(96,165,250,0.18)",
          transform: "rotate(-3deg)",
          overflow: "hidden",
        }}
      >
        <div
          style={{
            height: 0.053 * H,
            background: C.surface,
            borderBottom: `1px solid ${C.border}`,
            display: "flex",
            alignItems: "center",
            padding: `0 ${14 * u}px`,
            gap: 7 * u,
          }}
        >
          {["#FF5F57", "#FEBC2E", "#28C840"].map((c) => (
            <div key={c} style={{ width: 10 * u, height: 10 * u, borderRadius: 5 * u, background: c }} />
          ))}
          <div style={{ marginLeft: 10 * u, fontSize: 13 * u, color: C.subtle, fontFamily: MONO }}>
            Deepseek-Desktop-Agent
          </div>
        </div>
        <div
          style={{
            padding: `${16 * u}px ${18 * u}px`,
            display: "flex",
            flexDirection: "column",
            gap: 9 * u,
          }}
        >
          <Line who="你" text="帮我安装 Remotion 并启动 Studio" color={C.text} />
          <Line who="DeepSeek" text="正在调用本机 shell…" color={C.accent} />
          <div
            style={{
              marginTop: 6 * u,
              padding: `${11 * u}px ${13 * u}px`,
              borderRadius: 9 * u,
              background: "#0A0E14",
              border: `1px solid ${C.border}`,
              fontFamily: MONO,
              fontSize: 14 * u,
              lineHeight: 1.8,
              color: C.text,
            }}
          >
            <div style={{ color: C.ok }}>PS&gt; npx remotion studio</div>
            <div style={{ color: C.subtle }}>✓ 已启动 · http://localhost:3001</div>
            <div style={{ color: C.ok }}>退出码 0</div>
          </div>
          <Line who="DeepSeek" text="结果已回传对话，继续下一步？" color={C.text} />
        </div>
      </div>

      {/* ¥0 贴纸 */}
      <div
        style={{
          position: "absolute",
          left: 0.611 * W,
          top: 0.569 * H,
          width: 0.218 * H,
          height: 0.218 * H,
          borderRadius: "50%",
          background: `linear-gradient(140deg, ${C.accent2}, ${C.accent})`,
          boxShadow: "0 18px 50px rgba(77,107,254,0.5)",
          transform: "rotate(-7deg)",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          color: "#fff",
        }}
      >
        <div style={{ fontSize: 60 * u, fontWeight: 800, lineHeight: 1 }}>¥0</div>
        <div style={{ fontSize: 17 * u, fontWeight: 700, marginTop: 6 * u, letterSpacing: 1 * u }}>每次调用</div>
      </div>

      <AbsoluteFill style={{ border: "2px solid rgba(96,165,250,0.25)" }} />
      <AbsoluteFill
        style={{ background: "radial-gradient(1000px 760px at 50% 50%, transparent 45%, rgba(0,0,0,0.5) 100%)" }}
      />
    </AbsoluteFill>
  );
};
