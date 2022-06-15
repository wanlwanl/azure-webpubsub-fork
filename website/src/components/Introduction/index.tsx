import React from 'react'
import { Image, ImageFit, Label, Stack, StackItem, PrimaryButton, getHighContrastNoAdjustStyle } from '@fluentui/react'
import * as styles from './styles.module'

function LaunchDemo(): JSX.Element {
  return (
    <Stack grow horizontal horizontalAlign="start" styles={styles.left}>
      <StackItem styles={styles.leftItem}>
        <div style={styles.content}>
          <Label style={styles.title}>Scoreboard demo</Label>
          <Label>description to the demo</Label>
        </div>
        <PrimaryButton text="Launch demo"></PrimaryButton>
      </StackItem>
    </Stack>
  )
}

function DemoPreview(): JSX.Element {
  return (
    <Stack grow horizontal horizontalAlign="end">
      <Image imageFit={ImageFit.none} src="/img/introduction.png" alt="demo introduction" style={styles.preview}></Image>
    </Stack>
  )
}

export interface IntroductionProps {
  hidden: boolean
}

export default function Introduction(props: IntroductionProps): JSX.Element {
  console.log('hidden', props.hidden, props.hidden || false)
  const style = {}
  if (props.hidden) style['display'] = 'none'
  return (
    <div style={style}>
      <Stack horizontal wrap styles={styles.background}>
        <LaunchDemo></LaunchDemo>
        <DemoPreview></DemoPreview>
      </Stack>
    </div>
  )
}
