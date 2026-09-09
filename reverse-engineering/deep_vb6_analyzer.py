import struct, os, sys, json, re
from collections import defaultdict


def u8(b,o): return b[o]
def u16(b,o): return struct.unpack_from('<H', b, o)[0]
def i16(b,o): return struct.unpack_from('<h', b, o)[0]
def u32(b,o): return struct.unpack_from('<I', b, o)[0]
def i32(b,o): return struct.unpack_from('<i', b, o)[0]

def cstr(b,o,limit=1024):
    if o is None or o < 0 or o >= len(b): return None
    e=b.find(b'\0',o,min(len(b),o+limit))
    if e<0: e=min(len(b),o+limit)
    return b[o:e].decode('latin1','replace')

def parse_pe(b):
    pe=u32(b,0x3c); nsec=u16(b,pe+6); optsz=u16(b,pe+20); opt=pe+24
    ib=u32(b,opt+28); size_img=u32(b,opt+56); sec=opt+optsz
    sections=[]
    for i in range(nsec):
        o=sec+i*40
        name=b[o:o+8].split(b'\0')[0].decode('ascii','replace')
        vsize=u32(b,o+8); va=u32(b,o+12); rawsize=u32(b,o+16); raw=u32(b,o+20)
        sections.append(dict(name=name,va=va,vsize=vsize,raw=raw,rawsize=rawsize))
    return ib,size_img,sections

def va2off(va, ib, secs):
    rva=va-ib
    if rva<0: return None
    for s in secs:
        if s['va'] <= rva < s['va'] + max(s['vsize'],s['rawsize']):
            return s['raw'] + (rva-s['va'])
    if rva < min([s['va'] for s in secs] or [0x1000]): return rva
    return None

def off2va(off, ib, secs):
    for s in secs:
        if s['raw'] <= off < s['raw']+s['rawsize']:
            return ib + s['va'] + (off-s['raw'])
    return ib+off

def find_vb_header(b,ib,secs):
    p=0
    while True:
        p=b.find(b'VB5!',p)
        if p<0: return None
        if p+0x68 <= len(b):
            pdva=u32(b,p+0x30); pdo=va2off(pdva,ib,secs)
            if pdo is not None and pdo+0x23c<=len(b) and u32(b,pdo)==0x1f4:
                return p,pdva,pdo
        p+=1

def valid_name_ptr(va, b, ib, secs):
    o=va2off(va,ib,secs)
    if o is None or o>=len(b): return False
    s=cstr(b,o,256)
    if not s or len(s)>200: return False
    # VB identifiers are mostly ansi letters/digits/_; allow a few symbols for generated members
    return all((32 <= ord(ch) < 127) or (ord(ch)>=160) for ch in s)

FORM_EVENTS = {
0:'DragDrop',1:'DragOver',2:'LinkClose',3:'LinkError',4:'LinkExecute',5:'LinkOpen',
6:'Load',7:'Resize',8:'Unload',9:'QueryUnload',10:'Activate',11:'Deactivate',12:'Click',13:'DblClick',
14:'GotFocus',15:'KeyDown',16:'KeyPress',17:'KeyUp',18:'LostFocus',19:'MouseDown',20:'MouseMove',21:'MouseUp',
22:'Paint',23:'Initialize',24:'Terminate',25:'OLEDragOver',26:'OLEDragDrop',27:'OLEGiveFeedback',28:'OLEStartDrag',29:'OLESetData',30:'OLECompleteDrag'}
CMD_EVENTS={0:'Click',1:'DragDrop',2:'DragOver',3:'GotFocus',4:'KeyDown',5:'KeyPress',6:'KeyUp',7:'LostFocus',8:'MouseDown',9:'MouseMove',10:'MouseUp',11:'OLEDragOver',12:'OLECompleteDrag',13:'OLEGiveFeedback',14:'OLEStartDrag',15:'OLESetData',16:'OLEDragDrop'}
LABEL_EVENTS={0:'Change',1:'Click',2:'DblClick',3:'DragDrop',4:'DragOver',5:'LinkClose',6:'LinkError',7:'LinkOpen',8:'MouseDown',9:'MouseMove',10:'MouseUp',11:'LinkNotify',12:'OLEDragOver',13:'OLEDragDrop',14:'OLEGiveFeedback',15:'OLEStartDrag',16:'OLESetData',17:'OLECompleteDrag'}
PIC_EVENTS={0:'Change',1:'Click',2:'DblClick',3:'DragDrop',4:'DragOver',5:'GotFocus',6:'KeyDown',7:'KeyPress',8:'KeyUp',9:'LinkClose',10:'LinkError',11:'LinkOpen',12:'LostFocus',13:'MouseDown',14:'MouseMove',15:'MouseUp',16:'Paint',17:'LinkNotify',18:'Resize',19:'OLEDragOver',20:'OLEDragDrop',21:'OLEGiveFeedback',22:'OLEStartDrag',23:'OLESetData',24:'OLECompleteDrag',25:'Validate'}
SCROLL_EVENTS={0:'Change',1:'DragDrop',2:'DragOver',3:'GotFocus',4:'KeyDown',5:'KeyPress',6:'KeyUp',7:'LostFocus',8:'Scroll',9:'Validate'}

def event_name(control, idx, count):
    n=control or ''
    ln=n.lower()
    if n=='Form' or n=='MDIForm': mp=FORM_EVENTS
    elif count==17 and (ln.startswith('cmd') or 'button' in ln): mp=CMD_EVENTS
    elif count==18 and (ln.startswith('lbl') or ln.startswith('label')): mp=LABEL_EVENTS
    elif count==26 and (ln.startswith('picture') or ln.startswith('pic')): mp=PIC_EVENTS
    elif count==10 and ('scroll' in ln): mp=SCROLL_EVENTS
    elif ln.startswith('mnu'): mp={0:'Click'}
    else: mp={}
    ev=mp.get(idx)
    return f'{n}_{ev}' if ev else f'{n}_Event{idx}'

class Analyzer:
    def __init__(self,path):
        self.path=path; self.b=open(path,'rb').read(); self.ib,self.size_img,self.secs=parse_pe(self.b)
        h=find_vb_header(self.b,self.ib,self.secs)
        if not h: raise RuntimeError('VB header not found')
        self.h,self.pdva,self.pdo=h
        self.start=u32(self.b,self.pdo+0x0c); self.end=u32(self.b,self.pdo+0x10); self.native=u32(self.b,self.pdo+0x20)
        self.objtab_va=u32(self.b,self.pdo+4); self.submain=u32(self.b,self.h+0x2c)
        self.objects=[]; self.event_targets=defaultdict(list); self.event_named=defaultdict(list)
        self.discovered=defaultdict(set)

    def rv(self,va): return va2off(va,self.ib,self.secs)
    def rstr(self,va):
        o=self.rv(va); return cstr(self.b,o) if o is not None else None
    def dwordva(self,va):
        o=self.rv(va); return u32(self.b,o) if o is not None and o+4<=len(self.b) else 0

    def parse_objects(self):
        ot=self.rv(self.objtab_va); cnt=u16(self.b,ot+0x2a); arrva=u32(self.b,ot+0x30); arr=self.rv(arrva)
        for i in range(cnt):
            o=arr+i*0x30
            info=u32(self.b,o); nameva=u32(self.b,o+0x18); pc=u32(self.b,o+0x1c); namesva=u32(self.b,o+0x20); typ=u32(self.b,o+0x28)
            name=self.rstr(nameva) or f'Object{i}'
            names=[]
            no=self.rv(namesva) if namesva else None
            if no is not None and pc<10000:
                for j in range(pc):
                    p=u32(self.b,no+j*4); names.append(self.rstr(p) if p else None)
            obj=dict(index=i,name=name,info=info,pc=pc,namesva=namesva,type=typ,names=names,controls=[],event_links=[])
            if info and (typ & 2): self.parse_optional(obj)
            self.objects.append(obj)
        return self.objects

    def parse_optional(self,obj):
        oo=self.rv(obj['info']+0x38)
        if oo is None or oo+0x40>len(self.b): return
        cc=u32(self.b,oo+0x20); ctrlva=u32(self.b,oo+0x24); ec=u16(self.b,oo+0x28); linksva=u32(self.b,oo+0x30)
        obj['control_count']=cc; obj['event_count']=ec; obj['control_array_va']=ctrlva; obj['event_links_va']=linksva
        # Native event link table -> E9 thunk targets
        lo=self.rv(linksva) if linksva else None
        if lo is not None and 0<ec<4096:
            for slot in range(ec):
                raw=u32(self.b,lo+slot*4)
                tgt=0; op=None
                ro=self.rv(raw) if raw else None
                if ro is not None and ro+5<=len(self.b):
                    op=self.b[ro]
                    if op==0xE9: tgt=(raw+5+i32(self.b,ro+1)) & 0xffffffff
                obj['event_links'].append(dict(slot=slot,raw=raw,target=tgt,opcode=op))
                if tgt:
                    self.event_targets[obj['name']].append((slot,raw,tgt)); self.discovered[obj['name']].add(tgt)
        # controls and event pointers
        co=self.rv(ctrlva) if ctrlva else None
        if co is not None and 0<cc<5000:
            for ci in range(cc):
                c=co+ci*0x28
                evcnt=u16(self.b,c+2); guidva=u32(self.b,c+8); idx=i16(self.b,c+0x0c); evtva=u32(self.b,c+0x18); nameva=u32(self.b,c+0x20)
                name=self.rstr(nameva) or f'Control{ci}'
                ctrl=dict(index=ci,name=name,event_count=evcnt,event_table_va=evtva,array_index=idx,events=[])
                eto=self.rv(evtva) if evtva else None
                if eto is not None and 0<=evcnt<512:
                    # tEventTable size 0x18, then dword pointers
                    for ei in range(evcnt):
                        ptr=u32(self.b,eto+0x18+ei*4)
                        if not ptr: continue
                        match_raw=(ptr+8)&0xffffffff
                        match=next((x for x in obj['event_links'] if x['raw']==match_raw and x['target']),None)
                        nm=event_name(name,ei,evcnt)
                        e=dict(event_index=ei,pointer=ptr,name=nm,target=(match['target'] if match else 0),slot=(match['slot'] if match else None))
                        ctrl['events'].append(e)
                        if match:
                            self.event_named[obj['name']].append((nm,match['target'],match['slot'],ci,ei))
                obj['controls'].append(ctrl)

    def scan_procs(self):
        so=self.rv(self.start); eo=self.rv(self.end)
        if so is None or eo is None or eo<=so: raise RuntimeError(f'bad code range {self.start:x}-{self.end:x}')
        code=self.b[so:eo]
        seen=set([self.submain])
        for vals in self.discovered.values(): seen.update(vals)
        new=[]
        # standard prologue
        p=0
        while True:
            p=code.find(b'\x55\x8b\xec',p)
            if p<0: break
            va=self.start+p
            if va not in seen: seen.add(va); new.append(va)
            p+=1
        # frameless E8 targets
        plausible=set(range(0x50,0x58))|{0x6a,0x68,0x8b,0x89,0x8d,0x33,0xa1,0x66,0x83,0xff}|set(range(0xb8,0xc0))|set(range(0xd8,0xe0))
        for j in range(0,len(code)-5):
            if code[j]!=0xE8: continue
            rel=struct.unpack_from('<i',code,j+1)[0]; tgt=self.start+j+5+rel
            if not (self.start<tgt<self.end) or tgt in seen: continue
            ti=tgt-self.start
            if ti<4 or ti>=len(code): continue
            pb=code[ti-1]; boundary=(pb in (0x90,0xc3,0xcc)) or (ti>=3 and code[ti-3]==0xc2)
            if boundary and code[ti] in plausible:
                seen.add(tgt); new.append(tgt)
        new=sorted(set(new))
        # assign by anchor regions following Semi-VB-Decompiler heuristic
        n=len(self.objects)
        anchors=[0]*n; ismod=[False]*n; pc=[0]*n
        for i,o in enumerate(self.objects):
            vals=sorted(self.discovered[o['name']]); anchors[i]=vals[0] if vals else 0; ismod[i]=(o['type']&2)==0; pc[i]=o['pc']
        segs=[(self.start,-1)] + [(anchors[i],i) for i in range(n) if anchors[i]]
        segs=sorted(set(segs), key=lambda x:x[0])
        # If first real anchor == start, remove synthetic duplicate start/-1 to avoid empty ambiguity
        # keep synthetic only when it covers leading modules.
        for si,(rs,lead) in enumerate(segs):
            re_=segs[si+1][0] if si+1<len(segs) else self.end
            nextlead=segs[si+1][1] if si+1<len(segs) else n
            regs=[x for x in new if rs<=x<re_]
            if not regs: continue
            mods=[i for i in range(lead+1,nextlead) if 0<=i<n and ismod[i]]
            if not mods:
                if lead>=0:
                    self.discovered[self.objects[lead]['name']].update(regs)
                continue
            tot=sum(pc[i] for i in mods); shares=[]
            if tot:
                shares=[len(regs)*pc[i]//tot for i in mods]
            else:
                shares=[len(regs)//len(mods)]*len(mods)
            left=len(regs)-sum(shares); k=0
            while left>0:
                shares[k%len(shares)]+=1; k+=1; left-=1
            q=0
            for mi,sh in zip(mods,shares):
                self.discovered[self.objects[mi]['name']].update(regs[q:q+sh]); q+=sh
            if q<len(regs): self.discovered[self.objects[mods[-1]]['name']].update(regs[q:])
        return new

    def link_names(self):
        result=[]
        for o in self.objects:
            addrs=sorted(self.discovered[o['name']]); pc=o['pc']; names=o['names']
            mapped=[]; reason=''
            if not addrs:
                reason='no discovered native procedures'
            elif len(addrs)>pc:
                reason=f'discovered {len(addrs)} > ProcCount {pc}'
            else:
                base=pc-len(addrs)
                leading=[x for x in names[:base] if x]
                if leading:
                    reason=f'leading named phantom slots ({len(leading)}) prevent safe positional mapping'
                else:
                    for i,nm in enumerate(names):
                        ai=i-base
                        if nm and 0<=ai<len(addrs): mapped.append((nm,addrs[ai]))
                    reason='positional native mapping'
            # event names are authoritative where matched; merge, de-dupe
            evs=[(nm,va) for nm,va,*_ in self.event_named[o['name']]]
            byva=defaultdict(list)
            for nm,va in mapped+evs: byva[va].append(nm)
            result.append(dict(name=o['name'],proc_count=pc,discovered=addrs,mapped=mapped,events=evs,byva={f'0x{k:08X}':sorted(set(v)) for k,v in sorted(byva.items())},reason=reason))
        return result

    def run(self):
        self.parse_objects(); new=self.scan_procs(); linked=self.link_names()
        return dict(file=self.path,image_base=self.ib,code_start=self.start,code_end=self.end,submain=self.submain,project_data=self.pdva,objects=self.objects,linked=linked,new_prologues=new)

def main():
    path=sys.argv[1]; out=sys.argv[2] if len(sys.argv)>2 else None
    a=Analyzer(path); r=a.run()
    if out:
        with open(out,'w',encoding='utf-8') as f: json.dump(r,f,indent=2,ensure_ascii=False)
    print(f"{os.path.basename(path)} VB6 native: code 0x{r['code_start']:08X}-0x{r['code_end']:08X}, SubMain 0x{r['submain']:08X}")
    for x in r['linked']:
        if x['mapped'] or x['events']:
            print(f"\n{x['name']} ProcCount={x['proc_count']} discovered={len(x['discovered'])} [{x['reason']}]")
            for va,nms in [(k,v) for k,v in x['byva'].items()]: print(f"  {va}: {', '.join(nms)}")

if __name__=='__main__': main()
