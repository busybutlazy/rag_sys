import type { Config } from 'tailwindcss'

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        sumi: '#292524',
        washi: '#fafaf9',
        matcha: '#6f7d5f',
        sakura: '#d9a6a1',
        ink: '#1D2025',
        'ink-mid': '#4F545A',
        'ink-soft': '#737B84',
        paper: '#ffffff',
        page: '#F5F2EB',
        tint: '#EDEAE0',
        fog: '#E3DED4',
        rail: '#23272E',
      },
    },
  },
  plugins: [],
} satisfies Config
