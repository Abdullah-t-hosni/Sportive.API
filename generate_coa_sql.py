"""Legacy: يقرأ coa_data.txt. للمزامنة مع Accounts.xlsx استخدم: python tools/sync_coa_from_xlsx.py"""
import csv
import json

lines = []
with open('coa_data.txt', 'r', encoding='utf-8') as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        parts = line.split('\t')
        if len(parts) < 2:
            continue
        code = parts[0].strip()
        name = parts[1].strip()
        # Ensure name escapes single quotes
        name = name.replace("'", "\\'")
        
        type_str = parts[2].strip() if len(parts) > 2 else ""
        desc = parts[3].strip() if len(parts) > 3 else ""
        desc = desc.replace("'", "\\'")
        parent_raw = parts[4].strip() if len(parts) > 4 else ""
        posting = parts[5].strip() if len(parts) > 5 else "No"
        
        lines.append({
            'code': code,
            'name': name,
            'desc': desc,
            'posting': posting
        })

sql_statements = []
sql_statements.append("-- ══════════════════════════════════════════════════════")
sql_statements.append("-- Seed: شجرة الحسابات الكاملة")
sql_statements.append("-- قم بتشغيل هذا السكربت في قاعدة البيانات (مثل phpMyAdmin)")
sql_statements.append("-- ══════════════════════════════════════════════════════")
sql_statements.append("")
sql_statements.append("SET FOREIGN_KEY_CHECKS = 0;")
sql_statements.append("TRUNCATE TABLE `Accounts`;")
sql_statements.append("SET FOREIGN_KEY_CHECKS = 1;")
sql_statements.append("")

# Type: 1=Asset 2=Liability 3=Equity 4=Revenue 5=Expense
# Nature: 1=Debit 2=Credit

for acc in lines:
    c = acc['code']
    # Type depends on first digit
    if c.startswith('1'):
        t, n = 1, 1
    elif c.startswith('2'):
        t, n = 2, 2
    elif c.startswith('3'):
        t, n = 3, 2
    elif c.startswith('4'):
        t, n = 4, 2
    elif c.startswith('5'):
        t, n = 5, 1
    else:
        t, n = 1, 1
        
    level = len(c)
    if level == 1:
        lvl = 1
    elif level == 2:
        lvl = 2
    elif level == 4:
        lvl = 3
    elif level == 6:
        lvl = 4
    elif level == 3: # 511
        lvl = 3
    elif level == 5: # 51101
        lvl = 4
    elif level == 7: # 5210501
        lvl = 5
    else:
        lvl = level # arbitrary
        
    allow_posting = 1 if acc['posting'].lower() == 'yes' else 0
    # IsLeaf logic will be updated later: by default 1 unless it has children
    
    parent_code = "NULL"
    # Find parent code based on prefix
    # Common prefixes: 
    possible_parents = [acc2['code'] for acc2 in lines if len(acc2['code']) < len(c) and c.startswith(acc2['code'])]
    if possible_parents:
        parent_code = f"(SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='{max(possible_parents, key=len)}') x)"
    
    # IsLeaf = 0 if any other code starts with this code and is strictly longer
    is_leaf = 1
    has_children = [acc2['code'] for acc2 in lines if len(acc2['code']) > len(c) and acc2['code'].startswith(c)]
    if has_children:
        is_leaf = 0
        
    desc_val = f"'{acc['desc']}'" if acc['desc'] else "NULL"
        
    sql = f"INSERT INTO `Accounts` (`Code`,`NameAr`,`Description`,`Type`,`Nature`,`ParentId`,`Level`,`IsLeaf`,`AllowPosting`,`IsSystem`,`CreatedAt`)"
    sql += f" VALUES ('{c}', '{acc['name']}', {desc_val}, {t}, {n}, {parent_code}, {lvl}, {is_leaf}, {allow_posting}, 1, NOW());"
    sql_statements.append(sql)

with open('Run_AccountingSeed.sql', 'w', encoding='utf-8') as f:
    f.write("\n".join(sql_statements))

print("SQL script generated: Run_AccountingSeed.sql")
