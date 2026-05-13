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
      },
    },
  },
  plugins: [],
} satisfies Config
