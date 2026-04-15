const fs = require('fs');
const p = '../sportive-frontend/src/features/dashboard/ReportsPage.tsx';
let c = fs.readFileSync(p, 'utf8');

// 1. Add variant-reorder and cycle-count after inventory-aging
// The file uses single-space indent
const target1 = " { id: 'inventory-aging', icon: Calendar, ar: '\u0627\u0644\u0623\u0635\u0646\u0627\u0641 \u0627\u0644\u0631\u0627\u0643\u062f\u0629', en: 'Slow-Moving Stock' },";
const replace1 = target1 + "\n  { id: 'variant-reorder', icon: AlertTriangle, ar: '\u062a\u0646\u0628\u064a\u0647\u0627\u062a \u0646\u0642\u0635 \u0627\u0644\u0645\u0642\u0627\u0633\u0627\u062a', en: 'Size Shortage Alerts' },\n  { id: 'cycle-count', icon: CheckCircle2, ar: '\u0627\u0644\u062c\u0631\u062f \u0627\u0644\u062c\u0632\u0626\u064a \u0627\u0644\u064a\u0648\u0645\u064a', en: 'Daily Cycle Count' },";

// Check exact byte content
const idx = c.indexOf("id: 'inventory-aging'");
const start = idx - 5;
const end = idx + 80;
console.log('Around target:', JSON.stringify(c.slice(start, end)));

// Try finding with trimmed search
const trimSearch = "id: 'inventory-aging', icon: Calendar";
const found = c.indexOf(trimSearch);
if (found < 0) { console.log('STILL NOT FOUND'); process.exit(1); }

// Get full line including leading spaces
let lineStart = found;
while (lineStart > 0 && c[lineStart-1] !== '\n') lineStart--;
let lineEnd = c.indexOf('\n', found);
const fullLine = c.slice(lineStart, lineEnd);
console.log('Full line repr:', JSON.stringify(fullLine));

const newLine = fullLine + "\n  { id: 'variant-reorder', icon: AlertTriangle, ar: '\u062a\u0646\u0628\u064a\u0647\u0627\u062a \u0646\u0642\u0635 \u0627\u0644\u0645\u0642\u0627\u0633\u0627\u062a', en: 'Size Shortage Alerts' },\n  { id: 'cycle-count', icon: CheckCircle2, ar: '\u0627\u0644\u062c\u0631\u062f \u0627\u0644\u062c\u0632\u0626\u064a \u0627\u0644\u064a\u0648\u0645\u064a', en: 'Daily Cycle Count' },";
c = c.slice(0, lineStart) + newLine + c.slice(lineEnd);
console.log('Step 1 OK');

// 2. Add payables group before customer analytics
const customerIdx = c.indexOf("label: { ar: '\u062a\u062d\u0644\u064a\u0644 \u0627\u0644\u0639\u0645\u0644\u0627\u0621'");
if (customerIdx < 0) { console.log('CUSTOMER NOT FOUND'); process.exit(1); }

let blockStart = customerIdx;
while (blockStart > 0 && c[blockStart-1] !== '{') blockStart--;
blockStart--;

const payablesBlock = "{\n  label: { ar: '\u0627\u0644\u0645\u062f\u0641\u0648\u0639\u0627\u062a \u0648\u0627\u0644\u0627\u0633\u062a\u062d\u0642\u0627\u0642\u0627\u062a', en: 'Payables & Cash Flow' },\n  reports: [\n  { id: 'payables-schedule', icon: Calendar, ar: '\u062c\u062f\u0648\u0644 \u0627\u0633\u062a\u062d\u0642\u0627\u0642\u0627\u062a \u0627\u0644\u0645\u0648\u0631\u062f\u064a\u0646', en: 'Payables Schedule' },\n  ]\n },\n ";

c = c.slice(0, blockStart) + payablesBlock + c.slice(blockStart);
console.log('Step 2 OK');

fs.writeFileSync(p, c, 'utf8');
console.log('ALL DONE. Length:', c.length);
