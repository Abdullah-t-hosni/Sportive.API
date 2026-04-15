const fs = require('fs');
const p = '../sportive-frontend/src/features/dashboard/reports/OperationalReports.tsx';
let c = fs.readFileSync(p, 'utf8');

const newComponents = `
// ══════════════════════════════════════════════════════
// 13. جدول استحقاقات الموردين الأسبوعي (Payables Weekly Schedule)
// ══════════════════════════════════════════════════════
export function PayablesScheduleReport() {
  const { lang } = useUIStore()
  const [weeks, setWeeks] = useState(4)

  const { data, isLoading, refetch } = useQuery({
    queryKey: ['report-payables-schedule', weeks],
    queryFn: () => api.get('/operationalreports/payables-schedule', { params: { weeks } }).then(r => r.data),
  })

  const printSchedule = () => {
    const w = window.open('', '_blank', 'width=1100,height=800')
    if (!w || !data) return
    const buckets = data.buckets || []
    const rows = buckets.map((b: any) => \`
      <div style="margin-bottom:30px">
        <div style="background:\${b.isOverdue?'#fef2f2':'#f0fdf4'};border-left:4px solid \${b.isOverdue?'#ef4444':'#22c55e'};padding:12px 16px;margin-bottom:10px">
          <h3 style="margin:0;font-size:0.9em;color:\${b.isOverdue?'#b91c1c':'#166534'}">\${b.label}</h3>
          <p style="margin:4px 0 0;font-size:0.8em;color:#6b7280">\${b.count} فاتورة — إجمالي: \${b.totalDue.toLocaleString('ar-EG')} ج.م</p>
        </div>
        \${b.invoices.length > 0 ? \`<table style="width:100%;border-collapse:collapse">
          <thead><tr style="background:#f8fafc"><th style="padding:8px;text-align:right;font-size:0.75em">المورد</th><th style="padding:8px;text-align:right;font-size:0.75em">الفاتورة</th><th style="padding:8px;text-align:right;font-size:0.75em">تاريخ الاستحقاق</th><th style="padding:8px;text-align:right;font-size:0.75em">المتبقي</th></tr></thead>
          <tbody>\${b.invoices.map((inv: any) => \`<tr><td style="padding:8px;border-bottom:1px solid #f1f5f9;font-size:0.8em">\${inv.supplierName}</td><td style="padding:8px;border-bottom:1px solid #f1f5f9;font-size:0.8em;font-family:monospace">\${inv.invoiceNumber}</td><td style="padding:8px;border-bottom:1px solid #f1f5f9;font-size:0.8em">\${inv.dueDate||'—'}</td><td style="padding:8px;border-bottom:1px solid #f1f5f9;font-size:0.8em;font-weight:bold;color:#0f172a">\${(inv.remainingAmount||0).toLocaleString('ar-EG')} ج.م</td></tr>\`).join('')}</tbody>
        </table>\` : '<p style="color:#9ca3af;font-size:0.8em;padding:8px">لا توجد مدفوعات مجدولة في هذه الفترة</p>'}
      </div>\`).join('')
    w.document.write(\`<html dir="rtl"><head><meta charset="utf-8"><style>body{font-family:Arial;padding:40px;color:#0f172a}h1,h2,h3{margin:0}</style></head><body>
    <h2 style="margin-bottom:8px">جدول استحقاقات الموردين</h2>
    <p style="color:#6b7280;margin-bottom:30px">الإجمالي الكلي: \${(data.grandTotal||0).toLocaleString('ar-EG')} ج.م — متأخرات: \${(data.totalOverdue||0).toLocaleString('ar-EG')} ج.م</p>
    \${rows}</body></html>\`)
    w.document.close(); setTimeout(() => w.print(), 500)
  }

  return (
    <ReportShell
      title={lang === 'ar' ? 'جدول استحقاقات الموردين الأسبوعي' : 'Payables Weekly Schedule'}
      titleEn="Supplier Payment Schedule"
      isLoading={isLoading} onRefresh={refetch} onPrint={printSchedule}
      dateFilter={
        <div className="bg-white p-6 rounded-[2.5rem] border border-slate-100 shadow-xl flex items-center gap-8 animate-in fade-in slide-in-from-top-4 duration-700">
          <div className="space-y-2">
            <p className="text-xs font-bold text-slate-400 uppercase">{lang === 'ar' ? 'عدد الأسابيع القادمة' : 'FORECAST WEEKS'}</p>
            <div className="flex items-center gap-3">
              {[2, 4, 8, 12].map(w => (
                <button key={w} onClick={() => setWeeks(w)}
                  className={\`px-4 py-2 rounded-xl text-sm font-bold transition-all \${weeks === w ? 'bg-slate-900 text-white shadow-lg' : 'bg-slate-100 text-slate-600 hover:bg-slate-200'}\`}>
                  {w} {lang === 'ar' ? 'أسابيع' : 'wks'}
                </button>
              ))}
            </div>
          </div>
          <div className="flex-1 grid grid-cols-3 gap-4">
            {[
              { label: lang === 'ar' ? 'إجمالي المطلوب' : 'Grand Total', val: data?.grandTotal, color: 'text-slate-900', bg: 'bg-slate-50' },
              { label: lang === 'ar' ? 'متأخرات فورية' : 'Overdue Now', val: data?.totalOverdue, color: 'text-rose-600', bg: 'bg-rose-50' },
              { label: lang === 'ar' ? 'بدون تاريخ' : 'Undated', val: data?.undatedAmount, color: 'text-amber-600', bg: 'bg-amber-50' },
            ].map((k, i) => (
              <div key={i} className={\`\${k.bg} rounded-2xl p-4 border border-slate-100\`}>
                <p className="text-xs font-medium text-slate-400 mb-1">{k.label}</p>
                <p className={\`text-lg font-black font-mono tabular-nums \${k.color}\`}>{formatNum(k.val || 0, lang)} <span className="text-xs font-medium opacity-50">EGP</span></p>
              </div>
            ))}
          </div>
        </div>
      }
    >
      <div className="space-y-6">
        {(data?.buckets || []).map((bucket: any, bi: number) => (
          <motion.div key={bi} initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: bi * 0.05 }}
            className={\`bg-white border-2 rounded-[2rem] overflow-hidden shadow-sm \${bucket.isOverdue ? 'border-rose-100' : bucket.totalDue > 0 ? 'border-slate-100' : 'border-slate-50'}\`}>
            <div className={\`px-8 py-5 flex items-center justify-between \${bucket.isOverdue ? 'bg-rose-50' : 'bg-slate-50'}\`}>
              <div className="flex items-center gap-4">
                <div className={\`w-3 h-3 rounded-full \${bucket.isOverdue ? 'bg-rose-500 animate-pulse' : bucket.totalDue > 0 ? 'bg-emerald-400' : 'bg-slate-200'}\`} />
                <div>
                  <h3 className={\`font-bold text-sm \${bucket.isOverdue ? 'text-rose-700' : 'text-slate-800'}\`}>{bucket.label}</h3>
                  <p className="text-xs text-slate-400">{bucket.count} {lang === 'ar' ? 'فاتورة' : 'invoices'}</p>
                </div>
              </div>
              <div className="text-end">
                <p className="text-xs text-slate-400">{lang === 'ar' ? 'إجمالي مستحق' : 'Total Due'}</p>
                <p className={\`text-xl font-black font-mono tabular-nums \${bucket.isOverdue ? 'text-rose-600' : 'text-slate-900'}\`}>{formatNum(bucket.totalDue, lang)} <span className="text-sm opacity-40">EGP</span></p>
              </div>
            </div>
            {bucket.invoices.length > 0 && (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead>
                    <tr className="border-b border-slate-50 text-xs font-medium text-slate-400">
                      <th className="text-start px-8 py-3">{lang === 'ar' ? 'المورد' : 'Supplier'}</th>
                      <th className="text-start px-4 py-3">{lang === 'ar' ? 'الفاتورة' : 'Invoice'}</th>
                      <th className="text-center px-4 py-3">{lang === 'ar' ? 'الاستحقاق' : 'Due'}</th>
                      <th className="text-center px-4 py-3">{lang === 'ar' ? 'الأيام' : 'Days'}</th>
                      <th className="text-end px-8 py-3">{lang === 'ar' ? 'المتبقي' : 'Balance'}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-50">
                    {bucket.invoices.map((inv: any, ii: number) => (
                      <tr key={ii} className="hover:bg-slate-50/50 transition-all">
                        <td className="px-8 py-4 text-sm font-medium text-slate-900">{inv.supplierName}</td>
                        <td className="px-4 py-4 text-xs font-mono text-slate-400">{inv.invoiceNumber}</td>
                        <td className="px-4 py-4 text-center text-xs text-slate-500">{inv.dueDate || '—'}</td>
                        <td className="px-4 py-4 text-center">
                          <span className={\`text-xs font-bold px-2 py-1 rounded-lg \${bucket.isOverdue ? 'bg-rose-100 text-rose-700' : 'bg-emerald-50 text-emerald-700'}\`}>
                            {bucket.isOverdue ? '-' : '+'}{Math.abs(inv.daysOverdue ?? inv.daysUntilDue ?? 0)} {lang === 'ar' ? 'يوم' : 'd'}
                          </span>
                        </td>
                        <td className="px-8 py-4 text-end font-mono font-bold text-slate-900 tabular-nums">{formatNum(inv.remainingAmount, lang)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {bucket.invoices.length === 0 && (
              <div className="py-8 text-center opacity-30">
                <p className="text-sm font-medium text-slate-400">{lang === 'ar' ? 'لا توجد مدفوعات مجدولة' : 'No payments scheduled'}</p>
              </div>
            )}
          </motion.div>
        ))}
      </div>
    </ReportShell>
  )
}

// ══════════════════════════════════════════════════════
// 14. تنبيهات نقص المخزون على مستوى المقاس/اللون (Variant Reorder Alerts)
// ══════════════════════════════════════════════════════
export function VariantReorderReport() {
  const { lang } = useUIStore()
  const [threshold, setThreshold] = useState(2)
  const [zeroOnly, setZeroOnly] = useState(false)
  const [search, setSearch] = useState('')

  const { data, isLoading, refetch } = useQuery({
    queryKey: ['report-variant-reorder', threshold, zeroOnly],
    queryFn: () => api.get('/operationalreports/variant-reorder-alerts', { params: { threshold, zeroOnly } }).then(r => r.data),
  })

  const filtered = useMemo(() => {
    if (!data?.rows) return []
    if (!search.trim()) return data.rows
    const q = search.toLowerCase()
    return data.rows.filter((r: any) =>
      r.productName?.toLowerCase().includes(q) ||
      r.productSKU?.toLowerCase().includes(q) ||
      r.size?.toLowerCase().includes(q) ||
      r.color?.toLowerCase().includes(q)
    )
  }, [data, search])

  return (
    <ReportShell
      title={lang === 'ar' ? 'تنبيهات نقص المخزون (مستوى المقاس)' : 'Variant Reorder Alerts'}
      titleEn="Size & Color Stock Intelligence"
      isLoading={isLoading} onRefresh={refetch}
      dateFilter={
        <div className="bg-white p-6 rounded-[2.5rem] border border-slate-100 shadow-xl animate-in fade-in slide-in-from-top-4 duration-700">
          <div className="flex flex-wrap items-center gap-6">
            <div className="space-y-2">
              <p className="text-xs font-bold text-slate-400 uppercase">{lang === 'ar' ? 'حد النقص (قطع)' : 'ALERT THRESHOLD'}</p>
              <div className="flex items-center gap-2">
                {[1, 2, 3, 5, 10].map(t => (
                  <button key={t} onClick={() => setThreshold(t)}
                    className={\`w-10 h-10 rounded-xl text-sm font-bold transition-all \${threshold === t ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200'}\`}>
                    {t}
                  </button>
                ))}
              </div>
            </div>
            <div className="space-y-2">
              <p className="text-xs font-bold text-slate-400 uppercase">{lang === 'ar' ? 'فلتر' : 'FILTER'}</p>
              <button onClick={() => setZeroOnly(!zeroOnly)}
                className={\`px-4 py-2 rounded-xl text-sm font-bold transition-all \${zeroOnly ? 'bg-rose-600 text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200'}\`}>
                {lang === 'ar' ? 'مخزون صفر فقط' : 'Zero Stock Only'}
              </button>
            </div>
            <div className="flex-1 min-w-[200px] space-y-2">
              <p className="text-xs font-bold text-slate-400 uppercase">{lang === 'ar' ? 'بحث' : 'SEARCH'}</p>
              <div className="relative">
                <Search className="absolute start-4 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-300" />
                <input value={search} onChange={e => setSearch(e.target.value)}
                  placeholder={lang === 'ar' ? 'اسم الصنف أو SKU...' : 'Product or SKU...'}
                  className="w-full bg-slate-50 border border-slate-100 rounded-2xl py-2 ps-12 pe-4 text-sm outline-none focus:border-rose-300" />
              </div>
            </div>
            <div className="grid grid-cols-3 gap-3">
              {[
                { label: lang === 'ar' ? 'إجمالي التنبيهات' : 'Total Alerts', val: data?.totalAlerts, color: 'text-slate-900' },
                { label: lang === 'ar' ? 'مخزون صفر' : 'Zero Stock', val: data?.zeroStockCount, color: 'text-rose-600' },
                { label: lang === 'ar' ? 'حرج (1 قطعة)' : 'Critical (1pc)', val: data?.criticalCount, color: 'text-amber-600' },
              ].map((k, i) => (
                <div key={i} className="bg-slate-50 rounded-2xl p-3 text-center border border-slate-100">
                  <p className="text-[10px] font-medium text-slate-400 mb-1">{k.label}</p>
                  <p className={\`text-xl font-black font-mono \${k.color}\`}>{k.val ?? 0}</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      }
    >
      <div className="bg-white border border-slate-100 rounded-[3rem] shadow-xl overflow-hidden">
        <div className="px-8 py-6 bg-slate-950 text-white flex items-center gap-4">
          <div className="w-10 h-10 rounded-2xl bg-rose-500/20 flex items-center justify-center text-rose-400"><AlertTriangle className="w-5 h-5" /></div>
          <div>
            <h3 className="font-bold text-sm">{lang === 'ar' ? 'منبه نقص المقاسات والألوان' : 'Size & Color Shortage Radar'}</h3>
            <p className="text-xs opacity-30 mt-0.5">{filtered.length} {lang === 'ar' ? 'تنبيه نشط' : 'Active Alert(s)'}</p>
          </div>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full border-separate border-spacing-y-1.5 px-6 py-4">
            <thead>
              <tr className="text-xs text-slate-400 font-medium">
                <th className="text-start px-4 py-3">{lang === 'ar' ? 'الصنف' : 'Product'}</th>
                <th className="text-center px-4 py-3">{lang === 'ar' ? 'مقاس' : 'Size'}</th>
                <th className="text-center px-4 py-3">{lang === 'ar' ? 'لون' : 'Color'}</th>
                <th className="text-center px-4 py-3">{lang === 'ar' ? 'المخزون' : 'Stock'}</th>
                <th className="text-center px-4 py-3">{lang === 'ar' ? 'الحد الأدنى' : 'Min Level'}</th>
                <th className="text-center px-4 py-3">{lang === 'ar' ? 'النقص' : 'Shortage'}</th>
                <th className="text-start px-4 py-3">{lang === 'ar' ? 'الحالة' : 'Status'}</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((r: any, i: number) => (
                <tr key={i} className="group">
                  <td className="px-4 py-3 bg-slate-50 rounded-s-2xl border-y border-s border-white">
                    <p className="text-sm font-bold text-slate-900 truncate max-w-[180px]">{r.productName}</p>
                    <p className="text-xs font-mono text-slate-400">{r.productSKU}</p>
                  </td>
                  <td className="px-4 py-3 bg-white border-y border-white text-center">
                    {r.size ? <span className="text-xs font-bold bg-indigo-100 text-indigo-700 px-2 py-1 rounded-lg">{r.size}</span> : <span className="text-slate-300">—</span>}
                  </td>
                  <td className="px-4 py-3 bg-white border-y border-white text-center">
                    {r.color ? <span className="text-xs font-bold bg-sky-100 text-sky-700 px-2 py-1 rounded-lg">{r.color}</span> : <span className="text-slate-300">—</span>}
                  </td>
                  <td className="px-4 py-3 bg-white border-y border-white text-center">
                    <span className={\`text-lg font-black font-mono \${r.isZero ? 'text-rose-600' : r.isCritical ? 'text-amber-600' : 'text-slate-900'}\`}>{r.stock}</span>
                  </td>
                  <td className="px-4 py-3 bg-white border-y border-white text-center text-sm font-mono text-slate-400">{r.reorderLevel}</td>
                  <td className="px-4 py-3 bg-white border-y border-white text-center">
                    <span className="text-sm font-black text-rose-600 font-mono">-{r.shortage}</span>
                  </td>
                  <td className="px-4 py-3 bg-slate-50 rounded-e-2xl border-y border-e border-white">
                    {r.isZero
                      ? <span className="px-2 py-1 bg-rose-100 text-rose-700 text-xs font-bold rounded-lg">{lang === 'ar' ? 'نفد' : 'OUT'}</span>
                      : r.isCritical
                        ? <span className="px-2 py-1 bg-amber-100 text-amber-700 text-xs font-bold rounded-lg">{lang === 'ar' ? 'حرج' : 'CRITICAL'}</span>
                        : <span className="px-2 py-1 bg-orange-50 text-orange-600 text-xs font-bold rounded-lg">{lang === 'ar' ? 'منخفض' : 'LOW'}</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!filtered.length && (
            <div className="py-24 text-center opacity-30">
              <CheckCircle className="w-12 h-12 mx-auto text-emerald-300 mb-4" />
              <p className="text-sm font-medium text-slate-400">{lang === 'ar' ? 'كل المقاسات في المستوى المطلوب' : 'All sizes are at target stock levels'}</p>
            </div>
          )}
        </div>
      </div>
    </ReportShell>
  )
}

// ══════════════════════════════════════════════════════
// 15. الجرد الجزئي اليومي (Daily Cycle Count)
// ══════════════════════════════════════════════════════
export function CycleCountReport() {
  const { lang } = useUIStore()
  const [count, setCount] = useState(5)
  const [actualCounts, setActualCounts] = useState<Record<number, number | ''>>({})
  const [notes, setNotes] = useState<Record<number, string>>({})
  const [skipped, setSkipped] = useState<Set<number>>(new Set())
  const [submitted, setSubmitted] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [results, setResults] = useState<any>(null)

  const { data, isLoading, refetch } = useQuery({
    queryKey: ['cycle-count-today', count],
    queryFn: () => api.get('/operationalreports/cycle-count-today', { params: { count } }).then(r => r.data),
    staleTime: 1000 * 60 * 60, // 1 hour cache — consistent for the day
  })

  const activeItems = useMemo(() =>
    (data?.items || []).filter((it: any) => !skipped.has(it.variantId)),
    [data, skipped]
  )

  const canSubmit = activeItems.length > 0 && activeItems.every((it: any) => actualCounts[it.variantId] !== '' && actualCounts[it.variantId] !== undefined)

  const handleSubmit = async () => {
    setSubmitting(true)
    try {
      const entries = activeItems.map((it: any) => ({
        variantId: it.variantId,
        actualCount: Number(actualCounts[it.variantId] ?? it.systemStock),
        notes: notes[it.variantId] || undefined
      }))
      const res = await api.post('/operationalreports/cycle-count-submit', entries)
      setResults(res.data)
      setSubmitted(true)
    } catch (e: any) {
      import('react-hot-toast').then(({ default: toast }) =>
        toast.error(e?.response?.data?.message || 'Submission failed'))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <ReportShell
      title={lang === 'ar' ? 'الجرد الجزئي اليومي' : 'Daily Cycle Count'}
      titleEn="Continuous Inventory Accuracy"
      isLoading={isLoading} onRefresh={() => { refetch(); setSubmitted(false); setResults(null); setActualCounts({}); setSkipped(new Set()) }}
      dateFilter={
        <div className="bg-white p-6 rounded-[2.5rem] border border-slate-100 shadow-xl flex flex-wrap items-center gap-6 animate-in fade-in slide-in-from-top-4 duration-700">
          <div className="space-y-2">
            <p className="text-xs font-bold text-slate-400 uppercase">{lang === 'ar' ? 'عدد الأصناف اليومية' : 'DAILY ITEMS COUNT'}</p>
            <div className="flex items-center gap-2">
              {[3, 5, 10, 15].map(n => (
                <button key={n} onClick={() => setCount(n)}
                  className={\`px-4 py-2 rounded-xl text-sm font-bold transition-all \${count === n ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200'}\`}>
                  {n}
                </button>
              ))}
            </div>
          </div>
          <div className="flex-1 text-sm text-slate-500">
            <p>{lang === 'ar' ? 'تاريخ الجرد:' : 'Audit Date:'} <span className="font-bold text-slate-900">{data?.date || '—'}</span></p>
            <p className="text-xs text-slate-400 mt-1">{lang === 'ar' ? 'يتم اختيار الأصناف عشوائياً ومتسقاً لكل يوم' : 'Items are randomly but consistently selected per day'}</p>
          </div>
        </div>
      }
    >
      {submitted && results ? (
        <motion.div initial={{ opacity: 0, scale: 0.95 }} animate={{ opacity: 1, scale: 1 }} className="space-y-6">
          <div className="text-center py-8">
            <div className="w-20 h-20 bg-emerald-100 rounded-full flex items-center justify-center mx-auto mb-4">
              <CheckCircle className="w-10 h-10 text-emerald-600" />
            </div>
            <h3 className="text-xl font-bold text-slate-900">{lang === 'ar' ? 'تم تسليم الجرد بنجاح!' : 'Cycle Count Submitted!'}</h3>
            <p className="text-sm text-slate-400 mt-2">{results.withDifferences} {lang === 'ar' ? 'صنف به فرق في المخزون تم تعديله تلقائياً' : 'items had discrepancies and were auto-corrected'}</p>
          </div>
          <div className="space-y-3">
            {results.results?.map((r: any, i: number) => (
              <div key={i} className={\`p-4 rounded-2xl border flex items-center justify-between \${r.hasDifference ? 'bg-amber-50 border-amber-100' : 'bg-emerald-50 border-emerald-100'}\`}>
                <div className="flex items-center gap-3">
                  {r.hasDifference ? <AlertTriangle className="w-4 h-4 text-amber-500" /> : <CheckCircle className="w-4 h-4 text-emerald-500" />}
                  <div>
                    <p className="text-xs font-mono text-slate-400">Variant #{r.variantId}</p>
                    <p className="text-sm font-medium text-slate-900">{lang === 'ar' ? 'نظام:' : 'System:'} {r.oldStock} → {lang === 'ar' ? 'واقعي:' : 'Actual:'} {r.actualCount}</p>
                  </div>
                </div>
                <span className={\`text-sm font-black \${r.difference > 0 ? 'text-emerald-600' : r.difference < 0 ? 'text-rose-600' : 'text-slate-400'}\`}>
                  {r.difference > 0 ? '+' : ''}{r.difference}
                </span>
              </div>
            ))}
          </div>
          <button onClick={() => { setSubmitted(false); setResults(null); setActualCounts({}); setSkipped(new Set()); refetch() }}
            className="w-full h-12 bg-slate-900 text-white rounded-2xl text-sm font-bold hover:bg-black transition-all">
            {lang === 'ar' ? 'بدء جلسة جديدة' : 'Start New Session'}
          </button>
        </motion.div>
      ) : (
        <div className="space-y-4">
          <div className="flex items-center justify-between mb-2">
            <p className="text-sm font-medium text-slate-500">{lang === 'ar' ? 'أدخل العدد الفعلي لكل صنف:' : 'Enter actual count for each item:'}</p>
            <span className="text-xs text-slate-400">{activeItems.length}/{(data?.items || []).length} {lang === 'ar' ? 'صنف نشط' : 'active'}</span>
          </div>
          {(data?.items || []).map((it: any) => {
            const isSkipped = skipped.has(it.variantId)
            const val = actualCounts[it.variantId]
            const diff = val !== '' && val !== undefined ? Number(val) - it.systemStock : null
            return (
              <motion.div key={it.variantId} layout
                className={\`border-2 rounded-3xl p-5 transition-all \${isSkipped ? 'border-slate-100 bg-slate-50 opacity-40' : 'border-slate-100 bg-white hover:border-slate-200'}\`}>
                <div className="flex items-start justify-between gap-4">
                  <div className="flex-1">
                    <p className="font-bold text-slate-900 text-sm">{it.productName}</p>
                    <div className="flex items-center gap-1.5 mt-1">
                      <span className="text-[10px] font-mono text-slate-400 bg-slate-100 px-1.5 py-0.5 rounded">{it.sku}</span>
                      {it.size && <span className="text-[10px] font-bold bg-indigo-100 text-indigo-700 px-1.5 py-0.5 rounded">{it.size}</span>}
                      {it.color && <span className="text-[10px] font-bold bg-sky-100 text-sky-700 px-1.5 py-0.5 rounded">{it.color}</span>}
                    </div>
                    <p className="text-xs text-slate-400 mt-2">{lang === 'ar' ? 'مخزون النظام:' : 'System Stock:'} <span className="font-bold text-slate-700">{it.systemStock}</span></p>
                  </div>
                  <div className="flex items-center gap-3 shrink-0">
                    {!isSkipped && (
                      <div className="space-y-1 text-center">
                        <p className="text-[10px] font-medium text-slate-400">{lang === 'ar' ? 'العدد الفعلي' : 'ACTUAL'}</p>
                        <input
                          type="number" min="0"
                          value={val ?? ''}
                          onChange={e => setActualCounts(prev => ({ ...prev, [it.variantId]: e.target.value === '' ? '' : Number(e.target.value) }))}
                          className={\`w-20 h-12 text-center text-xl font-black rounded-2xl border-2 outline-none transition-all \${
                            diff === null ? 'border-slate-200 bg-slate-50'
                            : diff === 0 ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                            : diff > 0 ? 'border-blue-200 bg-blue-50 text-blue-700'
                            : 'border-rose-200 bg-rose-50 text-rose-700'
                          }\`}
                          placeholder="—"
                        />
                        {diff !== null && diff !== 0 && (
                          <p className={\`text-xs font-bold \${diff > 0 ? 'text-blue-600' : 'text-rose-600'}\`}>
                            {diff > 0 ? '+' : ''}{diff}
                          </p>
                        )}
                      </div>
                    )}
                    <button
                      onClick={() => setSkipped(prev => { const n = new Set(prev); if (n.has(it.variantId)) n.delete(it.variantId); else n.add(it.variantId); return n })}
                      className={\`h-10 px-3 rounded-xl text-xs font-bold transition-all \${isSkipped ? 'bg-slate-200 text-slate-600' : 'bg-slate-100 text-slate-400 hover:bg-slate-200'}\`}
                    >
                      {isSkipped ? (lang === 'ar' ? 'فعّل' : 'Undo') : (lang === 'ar' ? 'تخطي' : 'Skip')}
                    </button>
                  </div>
                </div>
                {!isSkipped && (
                  <div className="mt-3">
                    <input
                      type="text"
                      value={notes[it.variantId] || ''}
                      onChange={e => setNotes(prev => ({ ...prev, [it.variantId]: e.target.value }))}
                      placeholder={lang === 'ar' ? 'ملاحظة اختيارية...' : 'Optional note...'}
                      className="w-full text-xs bg-slate-50 border border-slate-100 rounded-xl px-3 py-2 outline-none focus:border-slate-300"
                    />
                  </div>
                )}
              </motion.div>
            )
          })}
          {(data?.items || []).length > 0 && (
            <button
              onClick={handleSubmit}
              disabled={!canSubmit || submitting}
              className="w-full h-14 bg-slate-900 hover:bg-black text-white rounded-3xl text-sm font-bold flex items-center justify-center gap-3 transition-all shadow-2xl disabled:opacity-40 disabled:cursor-not-allowed border-b-4 border-black"
            >
              {submitting ? <Loader2 className="w-5 h-5 animate-spin" /> : <CheckCircle2 className="w-5 h-5" />}
              {lang === 'ar' ? 'تأكيد الجرد وتحديث المخزون' : 'Submit Count & Update Stock'}
            </button>
          )}
        </div>
      )}
    </ReportShell>
  )
}
`;

c = c.trimEnd() + '\n' + newComponents + '\n';
fs.writeFileSync(p, c, 'utf8');
console.log('SUCCESS! Total chars:', c.length);
