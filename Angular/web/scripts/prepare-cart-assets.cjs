/* Format/size normalization only. Retouched sources are created with ImageGen. */
const sharp = require(process.argv[2] || 'sharp');
const { resolve } = require('node:path');
const root = resolve(__dirname, '../src/assets/brand');
const sourceRoot = resolve(__dirname, '../../../docs/brand-assets');
async function main() {
  for (const name of ['dominium', 'momentum', 'blanco', 'ancestral']) {
    const source = name === 'ancestral' ? resolve(root,'ancestral-bottle.png') : resolve(sourceRoot,`${name}-cart-source.png`);
    const destination = resolve(root,`${name}-cart.webp`);
    const result = await sharp(source).trim({ threshold:8 }).resize(288,912,{ fit:'contain', background:'#00000000' }).extend({ top:24,bottom:24,left:16,right:16,background:'#00000000' }).webp({ quality:92,alphaQuality:100 }).toFile(destination);
    const { data, info } = await sharp(destination).raw().toBuffer({ resolveWithObject:true });
    if (info.channels !== 4 || data[3] !== 0) throw Error(`${name}: expected true transparent edges`);
    console.log(`${name}: ${info.width}x${info.height}, alpha, ${(result.size / 1024).toFixed(1)} KiB`);
  }
  await sharp(resolve(root,'wicker-basket-cutout-v3.png')).resize(480,320,{fit:'contain',background:'#00000000'}).webp({quality:90,alphaQuality:100}).toFile(resolve(root,'wicker-basket-cart.webp'));
}
main().catch(error => { console.error(error); process.exitCode=1; });
