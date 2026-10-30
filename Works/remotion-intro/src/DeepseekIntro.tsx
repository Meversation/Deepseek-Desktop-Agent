import React from "react";
import { AbsoluteFill, Audio, Easing, interpolate, Series, staticFile, useCurrentFrame } from "remotion";
import { C, FONT } from "./theme";
import { BAR, TOTAL_BARS, useBarPulse, useBeatPulse } from "./beat";
import {
  ArchitectureScene,
  Fade,
  FlowScene,
  LoopScene,
  OutroScene,
  ReleaseScene,
  RiskScene,
  StabilityScene,
  TitleScene,
  ValueScene,
} from "./scenes";
import { UiInteractScene, UiStartScene } from "./uidemo";

const rand = (n: number) => {
  const x = Math.sin(n * 127.1 + 311.7) * 43758.5453;
  return x - Math.floor(x);
};

const PARTICLES = Array.from({ length: 28 }, (_, i) => ({
  x: rand(i + 1) * 1920,
  y: rand(i + 51) * 1080,
  size: 1.5 + rand(i + 91) * 2.6,
  speed: 0.3 + rand(i + 131) * 0.6,
  alpha: 0.08 + rand(i + 171) * 0.2,
}));

const Background: React.FC = () => {
  const frame = useCurrentFrame();
  const beat = useBeatPulse(4.2);
  const bar = useBarPulse(2.2);
  const shift = Math.sin(frame / 90) * 60;
  return (
    <AbsoluteFill style={{ overflow: "hidden" }}>
      <AbsoluteFill
        style={{
          background: `radial-gradient(1200px 800px at ${50 + shift / 12}% 18%, #16233A 0%, ${C.bg} 62%)`,
          transform: `scale(${1 + bar * 0.012})`,
        }}
      />
      <AbsoluteFill
        style={{
          backgroundImage:
            "linear-gradient(rgba(96,165,250,0.055) 1px, transparent 1px), linear-gradient(90deg, rgba(96,165,250,0.055) 1px, transparent 1px)",
          backgroundSize: "64px 64px",
          maskImage: "radial-gradient(circle at 50% 40%, black 20%, transparent 78%)",
          WebkitMaskImage: "radial-gradient(circle at 50% 40%, black 20%, transparent 78%)",
          transform: `scale(${1.012 + bar * 0.014})`,
        }}
      />
      <AbsoluteFill
        style={{
          background: `radial-gradient(620px 420px at ${20 + shift / 8}% 86%, rgba(77,107,254,${0.12 + beat * 0.16}), transparent 70%)`,
        }}
      />
      <AbsoluteFill style={{ background: `radial-gradient(900px 700px at 50% 50%, transparent 40%, rgba(0,0,0,0.55) 100%)` }} />
      {PARTICLES.map((p, i) => {
        const y = (((p.y - frame * p.speed) % 1120) + 1120) % 1120 - 20;
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              left: p.x,
              top: y,
              width: p.size,
              height: p.size,
              borderRadius: p.size,
              background: C.accent,
              opacity: p.alpha * (0.65 + beat * 0.35),
            }}
          />
        );
      })}
    </AbsoluteFill>
  );
};

/* 场景时长全部是小节（60 帧）的整数倍，切点落在重拍上 */
const SCENE_DURATIONS = [120, 120, 180, 120, 180, 120, 180, 180, 120, 240, 120];

export const TOTAL_FRAMES = TOTAL_BARS * BAR;

const SCENE_STARTS = SCENE_DURATIONS.reduce<number[]>((acc, d, i) => {
  acc.push(i === 0 ? 0 : acc[i - 1] + SCENE_DURATIONS[i - 1]);
  return acc;
}, []);

/* 切点冲击：亮部闪一下 + 一道横扫的光，让硬切更“踩点” */
const CutImpact: React.FC = () => {
  const frame = useCurrentFrame();
  let since = Infinity;
  for (const s of SCENE_STARTS) {
    if (frame >= s) since = Math.min(since, frame - s);
  }
  if (!Number.isFinite(since) || since > 8) {
    return null;
  }
  const flash = Math.exp(-since / 1.8);
  return (
    <AbsoluteFill style={{ pointerEvents: "none" }}>
      <AbsoluteFill
        style={{
          background: `radial-gradient(circle at 50% 46%, rgba(170,210,255,${0.3 * flash}), rgba(77,107,254,${0.14 * flash}) 38%, transparent 70%)`,
        }}
      />
      <AbsoluteFill
        style={{
          background: `linear-gradient(105deg, transparent 32%, rgba(255,255,255,${0.13 * flash}) 50%, transparent 68%)`,
        }}
      />
    </AbsoluteFill>
  );
};

/* 进场轻推：每个场景从 1.028 缓慢回落到 1，避免“静帧感” */
const Punch: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const frame = useCurrentFrame();
  const scale = interpolate(frame, [0, 10], [1.028, 1], {
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const y = interpolate(frame, [0, 10], [10, 0], {
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  return (
    <AbsoluteFill style={{ transform: `translateY(${y}px) scale(${scale})`, transformOrigin: "50% 50%" }}>
      {children}
    </AbsoluteFill>
  );
};

// 音量跟随信息密度：文字密集段（架构、稳定性、风险）压低，其余回升
const AUDIO_VOLUME_KEYS: Array<[number, number]> = [
  [0, 0],
  [8, 0.26],
  [240, 0.26],
  [250, 0.22],
  [420, 0.22],
  [430, 0.27],
  [540, 0.27],
  [550, 0.18],
  [720, 0.18],
  [730, 0.24],
  [840, 0.24],
  [850, 0.18],
  [1020, 0.18],
  [1200, 0.18],
  [1210, 0.26],
  [1320, 0.26],
  [1330, 0.22],
  [1550, 0.22],
  [1560, 0.28],
  [1670, 0.28],
  [1680, 0],
];

const volumeAt = (f: number) => {
  if (f <= AUDIO_VOLUME_KEYS[0][0]) return AUDIO_VOLUME_KEYS[0][1];
  for (let i = 1; i < AUDIO_VOLUME_KEYS.length; i++) {
    const [f1, v1] = AUDIO_VOLUME_KEYS[i];
    if (f <= f1) {
      const [f0, v0] = AUDIO_VOLUME_KEYS[i - 1];
      return interpolate(f, [f0, f1], [v0, v1]);
    }
  }
  return AUDIO_VOLUME_KEYS[AUDIO_VOLUME_KEYS.length - 1][1];
};

const scenes = [
  { duration: SCENE_DURATIONS[0], Comp: TitleScene },
  { duration: SCENE_DURATIONS[1], Comp: ValueScene },
  { duration: SCENE_DURATIONS[2], Comp: UiStartScene },
  { duration: SCENE_DURATIONS[3], Comp: FlowScene },
  { duration: SCENE_DURATIONS[4], Comp: ArchitectureScene },
  { duration: SCENE_DURATIONS[5], Comp: LoopScene },
  { duration: SCENE_DURATIONS[6], Comp: StabilityScene },
  { duration: SCENE_DURATIONS[7], Comp: RiskScene },
  { duration: SCENE_DURATIONS[8], Comp: ReleaseScene },
  { duration: SCENE_DURATIONS[9], Comp: UiInteractScene },
  { duration: SCENE_DURATIONS[10], Comp: OutroScene },
];

const Progress: React.FC = () => {
  const frame = useCurrentFrame();
  const progress = interpolate(frame, [0, TOTAL_FRAMES - 1], [0, 1], { extrapolateRight: "clamp" });
  return (
    <div style={{ position: "absolute", left: 0, right: 0, bottom: 0, height: 4 }}>
      {Array.from({ length: TOTAL_BARS }, (_, i) => (
        <div
          key={i}
          style={{
            position: "absolute",
            left: `${(i / TOTAL_BARS) * 100}%`,
            top: 0,
            width: 1,
            height: 6,
            background: "rgba(230,235,242,0.22)",
          }}
        />
      ))}
      <div
        style={{
          height: "100%",
          width: `${progress * 100}%`,
          background: `linear-gradient(90deg, ${C.accent2}, ${C.accent})`,
        }}
      />
    </div>
  );
};

export const DeepseekIntro: React.FC = () => {
  return (
    <AbsoluteFill style={{ backgroundColor: C.bg, fontFamily: FONT }}>
      <Background />
      <Audio src={staticFile("music/techno-120.mp3")} volume={volumeAt} />
      {/* 硬切 + 拍点闪光：每个场景独立进出场，切点对齐小节线 */}
      <Series>
        {scenes.map(({ duration, Comp }, i) => (
          <Series.Sequence key={i} durationInFrames={duration}>
            <Punch>
              <Fade duration={duration}>
                <Comp />
              </Fade>
            </Punch>
          </Series.Sequence>
        ))}
      </Series>
      <CutImpact />
      <Progress />
    </AbsoluteFill>
  );
};
