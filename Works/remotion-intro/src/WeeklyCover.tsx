import React from "react";
import { AbsoluteFill, Img, staticFile, useVideoConfig } from "remotion";
import { C, FONT, MONO } from "./theme";

/**
 * 周报封面 · 16:9 / 4:3 自适应
 * 复用 BilibiliCover 视觉系统：彩色渐变标题 + 终端卡片 + Chips + 光晕背景
 * 主题：更新日志 W44（v1.5.1 修复周）
 */
export const WeeklyCover: React.FC = () => {
  const { width: W, height: H } = useVideoConfig();
  const u = W / 1146; // 字号缩放基准（与 BilibiliCover 一致）

  const Chip: React.FC<{ children: React.ReactNode; accent?: boolean }> = ({ children, accent }) => (
    <div
      style={{
        padding: `${7 * u}px ${14 * u}px`,
        borderRadius: 999,
        border: `1px solid ${accent ? C.accent2 : C.border}`,
        background: accent ? "rgba(77,107,254,0.15)" : "rgba(26,32,43,0.9)",
        color: accent ? C.accent : C.text,
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
      {/* 背景：与 BilibiliCover 同款 */}
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

      {/* 左：更新日志文案区 */}
      <div style={{ position: "absolute", left: 0.054 * W, top: 0.09 * H, width: 0.5 * W }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12 * u }}>
          <Img src={staticFile("icon.png")} style={{ width: 34 * u, height: 34 * u, borderRadius: 8 * u }} />
          <span style={{ fontSize: 17 * u, letterSpacing: 3 * u, color: C.subtle, fontWeight: 700 }}>
            DEEPSEEK · DESKTOP AGENT · W44
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
          更新日志
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
          v1.5.1 修复周
        </div>

        <div style={{ marginTop: 20 * u, fontSize: 23 * u, color: C.text, fontWeight: 600 }}>
          多任务不串台 · 输入三级兜底 · 附件边界修复 · 急停保留队列
        </div>

        <div style={{ marginTop: 22 * u, display: "flex", gap: 12 * u, flexWrap: "wrap" }}>
          <Chip accent>9 项修复</Chip>
          <Chip>7 次提交</Chip>
          <Chip>真实界面验证</Chip>
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
          <span style={{ color: C.accent, fontWeight: 800 }}>10.25</span>
          <span>—</span>
          <span>10.31</span>
          <span>·</span>
          <span>输入链路与多任务绑定收口</span>
        </div>

        <div style={{ marginTop: 62 * u }}>
          <div style={{ height: 1, background: `linear-gradient(90deg, ${C.border}, transparent)` }} />
          <div style={{ marginTop: 16 * u, fontSize: 19 * u, color: C.subtle, letterSpacing: 0.5 * u }}>
            对话 · 多会话 · 工具调用 · 命令执行 · 紧急停止
          </div>
        </div>
      </div>

      {/* 右：终端卡片（更新日志摘要） */}
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
            W44 changelog
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
          <Line who="你" text="查看本周更新" color={C.text} />
          <Line who="Agent" text="W44 · v1.5.1 · 9 项修复" color={C.accent} />
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
            <div style={{ color: C.ok }}>✓ 多任务会话绑定收口</div>
            <div style={{ color: C.ok }}>✓ 输入三级兜底方案</div>
            <div style={{ color: C.ok }}>✓ 附件边界修复</div>
            <div style={{ color: C.ok }}>✓ 紧急停止保留队列</div>
            <div style={{ color: C.subtle }}>全部改动已在真实界面跑通</div>
          </div>
          <Line who="Agent" text="下周：连续跑测 + 诊断信息" color={C.text} />
        </div>
      </div>

      {/* W44 贴纸 */}
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
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          transform: "rotate(8deg)",
        }}
      >
        <div style={{ fontSize: 28 * u, fontWeight: 800, color: "#fff", letterSpacing: -1 * u }}>W44</div>
        <div style={{ fontSize: 13 * u, color: "rgba(255,255,255,0.75)", fontFamily: MONO, letterSpacing: 2 * u }}>
          v1.5.1
        </div>
      </div>
    </AbsoluteFill>
  );
};
