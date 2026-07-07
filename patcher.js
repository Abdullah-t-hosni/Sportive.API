const fs = require('fs');
const path = require('path');

function walk(dir) {
    let results = [];
    const list = fs.readdirSync(dir);
    list.forEach(file => {
        file = path.join(dir, file);
        const stat = fs.statSync(file);
        if (stat && stat.isDirectory()) { 
            results = results.concat(walk(file));
        } else if (file.endsWith('.cs')) { 
            results.push(file);
        }
    });
    return results;
}

const files = walk('D:/Sportive.API/Controllers');
let count = 0;
files.forEach(file => {
    let content = fs.readFileSync(file, 'utf8');
    let newContent = content.replace(/([ \t]+)(wb\.SaveAs\()/g, "$1Sportive.API.Utils.ExcelThemeHelper.ApplyElegantTheme(wb);\n$1$2");
    
    if (newContent !== content) {
        fs.writeFileSync(file, newContent, 'utf8');
        console.log('Updated ' + file);
        count++;
    }
});
console.log('Total files updated: ' + count);