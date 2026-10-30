import React from "react";
import { Composition } from "remotion";
import { DeepseekIntro, TOTAL_FRAMES } from "./DeepseekIntro";
import { BilibiliCover } from "./Cover";
import { QQAvatar } from "./QQAvatar";

export const Root: React.FC = () => {
  return (
    <>
      <Composition
        id="DeepseekIntro"
        component={DeepseekIntro}
        durationInFrames={TOTAL_FRAMES}
        fps={30}
        width={1920}
        height={1080}
      />
      <Composition
        id="BilibiliCover"
        component={BilibiliCover}
        durationInFrames={1}
        fps={30}
        width={1146}
        height={717}
      />
      <Composition
        id="BilibiliCover169"
        component={BilibiliCover}
        durationInFrames={1}
        fps={30}
        width={1920}
        height={1080}
      />
      <Composition
        id="BilibiliCover43"
        component={BilibiliCover}
        durationInFrames={1}
        fps={30}
        width={1440}
        height={1080}
      />
      <Composition
        id="QQAvatar"
        component={QQAvatar}
        durationInFrames={1}
        fps={30}
        width={1000}
        height={1000}
      />

    </>
  );
};
