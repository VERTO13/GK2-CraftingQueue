import fs from 'fs';
const src = fs.readFileSync(process.argv[2], 'utf8');
let h = src.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
const r = [
  [/\[b\]([\s\S]*?)\[\/b\]/g,'<b>$1</b>'], [/\[i\]([\s\S]*?)\[\/i\]/g,'<i>$1</i>'],
  [/\[color=(#[0-9a-f]+)\]([\s\S]*?)\[\/color\]/gi,'<span style="color:$1">$2</span>'],
  [/\[size=(\d)\]([\s\S]*?)\[\/size\]/g,(m,s,t)=>`<span style="font-size:${[0,10,13,16,20,26,32][+s]}px">${t}</span>`],
  [/\[center\]([\s\S]*?)\[\/center\]/g,'<div style="text-align:center">$1</div>'],
  [/\[img\](.*?)\[\/img\]/g,'<img src="$1" style="max-width:100%">'],
  [/\[url=(.*?)\]([\s\S]*?)\[\/url\]/g,'<a href="$1">$2</a>'],
  [/\[code\]([\s\S]*?)\[\/code\]/g,'<code>$1</code>'],
  [/\[list=1\]/g,'<ol>'], [/\[list\]/g,'<ul>'], [/\[\/list\]\n?/g,m=>m.startsWith('[/list]')?'</ul>':''],
  [/\[\*\]/g,'<li>'],
];
for (const [a,b] of r) h = h.replace(a,b);
h = h.replace(/<ol>([\s\S]*?)<\/ul>/g,'<ol>$1</ol>').replace(/\n/g,'<br>').replace(/<br>(<\/?(ul|ol|li|div))/g,'$1');
fs.writeFileSync(process.argv[3], `<!doctype html><meta charset="utf-8"><title>Nexus preview</title><body style="margin:0;background:#101010;color:#d7d7d7;font:15px/1.6 Roboto,Segoe UI,sans-serif"><div style="max-width:900px;margin:0 auto;padding:24px;background:#1b1b1b">${h}</div>`);
