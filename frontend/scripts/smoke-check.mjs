import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const loginPage = readFileSync(new URL('../src/pages/LoginPage.tsx', import.meta.url), 'utf8')
const protectedRoute = readFileSync(new URL('../src/components/ProtectedRoute.tsx', import.meta.url), 'utf8')

assert.match(loginPage, /<h1>Knowledge Desk<\/h1>/, 'login page heading should render')
assert.match(loginPage, /Invalid username or password/, 'login page should expose API error state')
assert.match(protectedRoute, /<Navigate to="\/login" replace \/>/, 'protected route should redirect guests')

console.log('frontend smoke checks passed')
