import { createCanvas } from '@napi-rs/canvas';
import { mkdirSync,writeFileSync } from 'node:fs';
const canvas=createCanvas(256,256),c=canvas.getContext('2d');
c.fillStyle='#6553b8';c.beginPath();c.roundRect(0,0,256,256,54);c.fill();
c.translate(35,39);c.scale(2.3,2.3);
c.fillStyle='#b4a1e3';c.beginPath();c.moveTo(12,19);c.lineTo(9,5);c.lineTo(30,15);c.quadraticCurveTo(40,11,51,15);c.lineTo(71,5);c.lineTo(67,23);c.bezierCurveTo(84,52,67,74,40,74);c.bezierCurveTo(13,74,-4,48,12,19);c.fill();
for(const x of [26,54]){c.fillStyle='white';c.beginPath();c.ellipse(x,34,18,20,0,0,Math.PI*2);c.fill();}
for(const x of [30,50]){c.fillStyle='#292344';c.beginPath();c.arc(x,35,8,0,Math.PI*2);c.fill();c.fillStyle='white';c.beginPath();c.arc(x+2,32,2.8,0,Math.PI*2);c.fill();}
c.fillStyle='#efb36a';c.beginPath();c.moveTo(33,51);c.lineTo(40,61);c.lineTo(47,51);c.closePath();c.fill();
const png=canvas.toBuffer('image/png');mkdirSync('build',{recursive:true});writeFileSync('build/icon.png',png);
const header=Buffer.alloc(22);header.writeUInt16LE(1,2);header.writeUInt16LE(1,4);header.writeUInt16LE(1,10);header.writeUInt16LE(32,12);header.writeUInt32LE(png.length,14);header.writeUInt32LE(22,18);writeFileSync('build/icon.ico',Buffer.concat([header,png]));
