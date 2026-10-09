import { Component, DestroyRef, ElementRef, effect, inject, input, viewChild } from '@angular/core';
import { Chart, ChartConfiguration, registerables } from 'chart.js';

Chart.register(...registerables);
Chart.defaults.font.family = "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', sans-serif";

/** Reads chart colours from the CSS tokens in styles.scss so light/dark mode stay in one place. */
export function chartTheme() {
  const css = getComputedStyle(document.documentElement);
  const v = (name: string) => css.getPropertyValue(name).trim();
  return {
    series1: v('--series-1'),
    series2: v('--series-2'),
    surface: v('--surface-1'),
    ink: v('--text-primary'),
    inkSecondary: v('--text-secondary'),
    muted: v('--text-muted'),
    grid: v('--grid'),
    baseline: v('--baseline'),
  };
}

/** Shared defaults: recessive grid and axes, system font, index tooltips. */
export function baseOptions(): ChartConfiguration['options'] {
  const t = chartTheme();
  return {
    responsive: true,
    maintainAspectRatio: false,
    animation: { duration: 250 },
    color: t.inkSecondary,
    plugins: {
      legend: { labels: { color: t.inkSecondary, boxWidth: 12, boxHeight: 12, useBorderRadius: true, borderRadius: 2 } },
      tooltip: { backgroundColor: t.ink, titleColor: t.surface, bodyColor: t.surface, padding: 10, cornerRadius: 6 },
    },
    scales: {
      x: { grid: { color: t.grid }, border: { color: t.baseline }, ticks: { color: t.muted } },
      y: { grid: { color: t.grid }, border: { color: t.baseline }, ticks: { color: t.muted } },
    },
  } as ChartConfiguration['options'];
}

/** Thin wrapper around a Chart.js instance; updates in place when the config input changes. */
@Component({
  selector: 'app-chart',
  template: `<div class="chart-box" [style.height.px]="height()">
    <canvas #canvas role="img" [attr.aria-label]="label()"></canvas>
  </div>`,
})
export class ChartComponent {
  readonly config = input.required<ChartConfiguration>();
  readonly height = input(260);
  readonly label = input('');

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  constructor() {
    effect(() => {
      const config = this.config();
      if (!this.chart) {
        this.chart = new Chart(this.canvas().nativeElement, config);
        return;
      }
      this.chart.data = config.data;
      this.chart.options = config.options ?? {};
      this.chart.update();
    });
    inject(DestroyRef).onDestroy(() => this.chart?.destroy());
  }
}
