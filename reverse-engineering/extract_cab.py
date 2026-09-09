import struct,sys,os,zlib
p=sys.argv[1]; out=sys.argv[2]
b=open(p,'rb').read()
u16=lambda o: struct.unpack_from('<H',b,o)[0]
u32=lambda o: struct.unpack_from('<I',b,o)[0]
assert b[:4]==b'MSCF'
cbCab=u32(8); coffFiles=u32(16); cFolders=u16(26); cFiles=u16(28); flags=u16(30)
pos=36; cbCFHeader=cbCFFolder=cbCFData=0
if flags & 4:
    cbCFHeader=u16(pos); cbCFFolder=b[pos+2]; cbCFData=b[pos+3]; pos+=4+cbCFHeader
if flags & 1: # prev cabinet strings
    for _ in range(2):
        pos=b.index(0,pos)+1
if flags & 2:
    for _ in range(2): pos=b.index(0,pos)+1
folders=[]
for i in range(cFolders):
    start=u32(pos); n=u16(pos+4); comp=u16(pos+6); pos+=8+cbCFFolder
    folders.append((start,n,comp))
files=[]; pos=coffFiles
for i in range(cFiles):
    size=u32(pos); off=u32(pos+4); fi=u16(pos+8); date=u16(pos+10); time=u16(pos+12); attr=u16(pos+14); pos+=16
    e=b.index(0,pos); name=b[pos:e].decode('latin1'); pos=e+1
    files.append((name,size,off,fi))
print('folders',folders,'files',len(files),'reserve',cbCFHeader,cbCFFolder,cbCFData)
# decompress each folder
folder_data=[]
for idx,(start,n,comp) in enumerate(folders):
    pos=start; outbuf=bytearray(); history=b''
    ctype=comp & 0x000f
    print('folder',idx,'ctype',ctype,'blocks',n)
    for bi in range(n):
        csum=u32(pos); cbData=u16(pos+4); cbUn=u16(pos+6); pos += 8+cbCFData
        dat=b[pos:pos+cbData]; pos+=cbData
        if ctype==0:
            dec=dat
        elif ctype==1:
            if not dat.startswith(b'CK'): raise ValueError(('bad CK',idx,bi,dat[:8]))
            raw=dat[2:]
            try:
                if history:
                    dobj=zlib.decompressobj(-15, zdict=history[-32768:])
                    dec=dobj.decompress(raw)+dobj.flush()
                else:
                    dec=zlib.decompress(raw,-15)
            except TypeError:
                # older py fallback
                dec=zlib.decompress(raw,-15)
            except zlib.error as e:
                # some blocks do not need dictionary
                try: dec=zlib.decompress(raw,-15)
                except Exception: raise RuntimeError(('zlib',idx,bi,e))
        else: raise NotImplementedError(('compression',ctype))
        if len(dec)!=cbUn: raise ValueError(('size',idx,bi,len(dec),cbUn))
        outbuf += dec; history=(history+dec)[-32768:]
    folder_data.append(bytes(outbuf))
os.makedirs(out,exist_ok=True)
for name,size,off,fi in files:
    if fi >= len(folder_data):
        print('skip spanning',name,fi); continue
    dat=folder_data[fi][off:off+size]
    if len(dat)!=size: print('short',name,len(dat),size)
    dest=os.path.join(out,name.replace('\\','/'))
    os.makedirs(os.path.dirname(dest) or out,exist_ok=True)
    open(dest,'wb').write(dat)
print('done')
