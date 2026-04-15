const fs = require('fs');
const p = '../sportive-frontend/src/features/dashboard/purchases/PurchaseInvoicesTab.tsx';
let c = fs.readFileSync(p, 'utf8');

const lines = c.split('\n');
// Find the last 3 lines: '   </AnimatePresence>', ' </div>', ' )', '}'
let insertIdx = -1;
for (let i = lines.length - 1; i >= 0; i--) {
  if (lines[i].trim() === '</AnimatePresence>') {
    insertIdx = i + 1;
    break;
  }
}

if (insertIdx < 0) { console.log('NOT FOUND'); process.exit(1); }

const newLines = [
  '',
  '    {/* BARCODE AUTO-PRINT MODAL */}',
  '    <AnimatePresence>',
  '      {barcodeModal && (',
  '        <div className="fixed inset-0 z-[120] flex items-center justify-center p-4">',
  '          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} onClick={() => setBarcodeModal(null)} className="absolute inset-0 bg-slate-950/50 backdrop-blur-sm" />',
  '          <motion.div initial={{ opacity: 0, scale: 0.9, y: 30 }} animate={{ opacity: 1, scale: 1, y: 0 }} exit={{ opacity: 0, scale: 0.9 }} className="relative w-full max-w-xl bg-white rounded-[2.5rem] shadow-2xl overflow-hidden flex flex-col max-h-[85vh]">',
  '            <div className="p-7 flex items-center justify-between bg-gradient-to-r from-emerald-600 to-teal-600 text-white">',
  '              <div className="flex items-center gap-4">',
  '                <div className="w-12 h-12 bg-white/20 rounded-2xl flex items-center justify-center"><QrCode className="w-6 h-6" /></div>',
  "                <div><h2 className=\"text-lg font-bold\">{lang==='ar'?'طباعة باركود المستلمات':'Print Stock Barcodes'}</h2><p className=\"text-xs opacity-70\">PO #{barcodeModal.invoiceNumber} · {barcodeModal.items.length} {lang==='ar'?'صنف':'items'}</p></div>",
  '              </div>',
  '              <button onClick={() => setBarcodeModal(null)} className="w-10 h-10 bg-white/20 hover:bg-white/30 rounded-xl flex items-center justify-center"><X className="w-5 h-5" /></button>',
  '            </div>',
  '            <div className="mx-6 mt-5 p-4 bg-amber-50 border border-amber-100 rounded-2xl flex items-start gap-3">',
  '              <AlertTriangle className="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />',
  "              <p className=\"text-xs font-medium text-amber-700\">{lang==='ar'?'تم تحديث المخزون. اطبع الباركود على الوحدات قبل عرضها على الرفوف.':'Inventory updated. Print barcodes before shelving.'}</p>",
  '            </div>',
  '            <div className="flex-1 overflow-y-auto p-6 space-y-3">',
  '              {barcodeModal.items.map((it: any, idx: number) => (',
  '                <div key={idx} className="flex items-center gap-3 p-4 bg-slate-50 border border-slate-100 rounded-2xl hover:border-emerald-200 transition-all">',
  '                  <div className="w-9 h-9 bg-white border border-slate-100 rounded-xl flex items-center justify-center shrink-0"><Package className="w-4 h-4 text-slate-400" /></div>',
  '                  <div className="flex-1 min-w-0">',
  '                    <p className="text-sm font-bold text-slate-900 truncate">{it.description}</p>',
  '                    <div className="flex items-center gap-1 mt-1">',
  "                      {it.sku && <span className=\"text-[10px] font-mono text-slate-400 bg-slate-100 px-1.5 py-0.5 rounded\">{it.sku}</span>}",
  "                      {it.size && <span className=\"text-[10px] font-bold bg-indigo-100 text-indigo-700 px-1.5 py-0.5 rounded\">{it.size}</span>}",
  "                      {it.color && <span className=\"text-[10px] font-bold bg-sky-100 text-sky-700 px-1.5 py-0.5 rounded\">{it.color}</span>}",
  '                    </div>',
  '                  </div>',
  '                  <div className="flex items-center gap-3 shrink-0">',
  "                    <div className=\"text-center\"><p className=\"text-[10px] text-slate-400\">{lang==='ar'?'كمية':'Qty'}</p><p className=\"text-lg font-black text-emerald-600\">{it.quantity}</p></div>",
  "                    <button onClick={() => { const v = it.productVariantId ? ('&variantId=' + it.productVariantId) : ''; window.open('/admin/barcodes?productId=' + it.productId + v + '&qty=' + it.quantity, '_blank'); }} className=\"h-9 px-4 bg-white border border-emerald-200 text-emerald-600 text-xs font-bold rounded-xl hover:bg-emerald-50 flex items-center gap-1.5\">",
  "                      <Printer className=\"w-3.5 h-3.5\" />{lang==='ar'?'طباعة':'Print'}",
  '                    </button>',
  '                  </div>',
  '                </div>',
  '              ))}',
  '            </div>',
  '            <div className="p-6 border-t border-slate-100 flex items-center justify-between gap-4 bg-slate-50/60">',
  "              <p className=\"text-xs text-slate-400\">{lang==='ar'?'أو صفحة الباركود للطباعة الجماعية':'Or bulk print from barcode page'}</p>",
  '              <div className="flex gap-3">',
  "                <button onClick={() => setBarcodeModal(null)} className=\"h-10 px-5 bg-white border border-slate-200 rounded-xl text-sm font-medium hover:bg-slate-50\">{lang==='ar'?'إغلاق':'Close'}</button>",
  "                <button onClick={() => { const ids = [...new Set(barcodeModal.items.map((it:any) => it.productId).filter(Boolean))].join(','); window.open('/admin/barcodes?bulkProducts=' + ids, '_blank'); }} className=\"h-10 px-5 bg-emerald-600 hover:bg-emerald-700 text-white rounded-xl text-sm font-bold flex items-center gap-2\">",
  "                  <QrCode className=\"w-4 h-4\" />{lang==='ar'?'طباعة الكل':'Print All'}",
  '                </button>',
  '              </div>',
  '            </div>',
  '          </motion.div>',
  '        </div>',
  '      )}',
  '    </AnimatePresence>',
];

lines.splice(insertIdx, 0, ...newLines);
fs.writeFileSync(p, lines.join('\n'), 'utf8');
console.log('SUCCESS. Total lines now:', lines.length);
