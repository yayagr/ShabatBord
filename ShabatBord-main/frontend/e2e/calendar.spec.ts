import { expect, test } from '@playwright/test'
import { AxeBuilder } from '@axe-core/playwright'
import type { Page } from '@playwright/test'

async function checkAccessibility(page: Page) {
  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()
  expect(violations.flatMap(issue => issue.nodes.map(node => `${issue.id}: ${node.target.join(', ')}: ${node.failureSummary}`))).toEqual([])
}

test.beforeEach(async ({ page }) => {
  let assignment: { date: string; familyName: string; version: string } | null = null
  await page.route('**/api/**', async route => {
    const request = route.request()
    if (request.method() === 'POST' || request.method() === 'PUT') {
      const body: { familyName: string } = request.postDataJSON()
      assignment = { date: '2027-10-30', familyName: body.familyName, version: crypto.randomUUID() }
      await route.fulfill({ status: request.method() === 'POST' ? 201 : 200, json: assignment })
    } else if (request.method() === 'DELETE') {
      assignment = null
      await route.fulfill({ status: 204 })
    } else {
      await route.fulfill({
        json: {
          year: 2027,
          shabbatot: [
            { date: '2027-10-30', hebrewDate: 'כ״ט תשרי תשפ״ח', reading: 'פרשת בראשית', isFestival: false, isMevarchim: true, blessedMonth: 'חשון', roshChodeshDates: ['2027-10-31', '2027-11-01'], assignment },
            { date: '2028-10-07', hebrewDate: 'י״ז תשרי תשפ״ט', reading: 'סוכות ג׳ (חוה״מ)', isFestival: true, isMevarchim: false, blessedMonth: null, roshChodeshDates: [], assignment: null },
          ],
        },
      })
    }
  })
})

test('keyboard family claim, edit and removal restore focus and meet accessibility checks', async ({ page }) => {
  await page.goto('/?year=2027')
  await expect(page.getByText('פרשת בראשית')).toBeVisible()
  await expect(page.getByLabel('מחזור המתחיל בשנת')).toHaveValue('2027')
  await expect(page.getByLabel('טווח תאריכי המחזור')).toContainText('07.10.2028')
  await checkAccessibility(page)
  const choose = page.getByRole('button', { name: 'בחרו שבת — 30.10.2027' })
  await choose.focus()
  await page.keyboard.press('Enter')
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByLabel('שם המשפחה')).toBeFocused()
  await checkAccessibility(page)
  await page.keyboard.press('Escape')
  await expect(dialog).not.toBeVisible()
  await expect(choose).toBeFocused()
  await choose.click()
  await dialog.getByLabel('שם המשפחה').fill('משפחת בדיקה')
  await dialog.getByRole('button', { name: 'שמירת השיבוץ' }).click()
  const edit = page.getByRole('button', { name: 'עריכת השיבוץ ל־30.10.2027' })
  await expect(edit).toBeFocused()
  await edit.click()
  await dialog.getByLabel('שם המשפחה').fill('משפחת שינוי')
  await dialog.getByRole('button', { name: 'שמירת השיבוץ' }).click()
  await expect(page.getByText('משפחת שינוי')).toBeVisible()
  await page.getByRole('button', { name: 'הסרת השיבוץ ל־30.10.2027' }).click()
  await dialog.getByRole('button', { name: 'כן, הסרת השיבוץ' }).click()
  await expect(choose).toBeFocused()
})

test('mobile layout has no page overflow and supports long family names', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/?year=2027')
  await expect(page.getByText('פרשת בראשית')).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.getByRole('button', { name: 'בחרו שבת — 30.10.2027' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('שם המשפחה').fill('א'.repeat(100))
  await dialog.getByRole('button', { name: 'שמירת השיבוץ' }).click()
  await expect(page.getByText('א'.repeat(100))).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await checkAccessibility(page)
})
