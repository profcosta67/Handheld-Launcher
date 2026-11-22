import math
NEG=float('-inf')
class C:
    RetFix=True
    Drop=False; DropHold=15; DownProbe=True
    Gate=True; Probe=True; ProbeEvery=60; ProbeGain=1.12
    NewSettle=True; UnreachDelay=30; UnreachMaxHold=240; LearnedHold=60
    BelowRatio=0.90; MeetRatio=0.95; IgnoreBelowFps=10; StepUp=3; StepDown=1; UpCd=4; DownAfter=12
    CoarseStep=2; CoarseDownAfter=8; FloorHold=180; Settle=30; MaxFloorHold=1800; IgnoreLow=20
    FutileClimb=6; FutileGain=1.05; FutileHold=120; NearN=4; ResumeHold=8; Proven=6; Surplus=1.25; ReturnAfter=8
    def __init__(s,target,mn,mx,start,learned=False,coarse=True):
        s.target=max(15,target); s.min=min(mn,mx); s.max=max(mn,mx)
        s.W=max(s.min,min(s.max,start)); s.floor=s.min; s.learned=learned; s.coarse=coarse
        s.below=0; s.goodSince=0.0; s.lastChange=0.0; s.floorUntil=NEG; s.started=False
        s.resumeTo=None; s.resumeAt=0.0; s.retClimb=None; s.proven=None; s.climbFromProbe=False
        s.settled=False; s.near=0; s.floorHold=C.FloorHold; s.floorConfirmed=False; s.lastDown=False; s.lowSince=None
        s.climbW=None; s.climbFps=0.0; s.climbFloor=0; s.climbFloorUntil=0.0; s.climbFloorConfirmed=False; s.climbCoarse=False
        s.futileUntil=NEG; s.futileHold=C.FutileHold; s.floorLearned=False; s.learnedCheck=False; s.eff=float(s.target); s.unreach=False; s.unreachUntil=NEG; s.best=0.0; s.hist=[]; s.maxSince=None; s.unreachSince=0.0; s.plateau=0.0; s.exitN=0; s.curve={}; s.dev=0; s.probeAt=None; s.probeBase=0.0; s.probeV=[]; s.lastProbe=NEG; s.upN=0; s.prevFps=None; s.dropUntil=NEG; s.probeDir=1; s.downBlockUntil=NEG

    CurveWindow=240; SceneDev=0.12; CurveMinN=3; CurveSettle=4
    def note(s,fps,now):
        if fps<C.IgnoreBelowFps or now-s.lastChange<C.CurveSettle: return
        m,n=s.cavg(s.W-1,s.W+1,now)
        if n>=C.CurveMinN and abs(fps-m)>C.SceneDev*m: s.dev+=1
        else: s.dev=0
        if s.dev>=3:
            # a cena mudou: o que se mediu antes não vale para agora
            s.curve={}; s.dev=0
            if s.unreach:
                s.unreach=False; s.eff=float(s.target); s.futileUntil=NEG; s.exitN=0; s.probeAt=None
        q=s.curve.setdefault(s.W,[]); q.append((now,fps))
        while q and now-q[0][0]>C.CurveWindow: q.pop(0)
        if len(q)>12: q.pop(0)
    def cavg(s,lo,hi,now):
        v=[f for w,q in s.curve.items() if lo<=w<=hi for (t,f) in q if now-t<=C.CurveWindow]
        return (sum(v)/len(v),len(v)) if v else (None,0)
    def knee(s,plateau,now):
        for w in sorted(s.curve):
            m,n=s.cavg(w,w+1,now)
            if n>=C.CurveMinN and m>=plateau*0.95: return w
        return None
    def mark(s,plateau,fps,now):
        s.unreach=True; s.unreachUntil=now+s.futileHold; s.unreachSince=now
        s.plateau=plateau; s.eff=max(15.0,min(float(s.target),s.plateau*0.97))
        s.futileFps=fps; s.futileUntil=now+s.futileHold; s.futileHold=min(C.UnreachMaxHold,s.futileHold*2)
        s.maxSince=None; s.climbW=None; s.probeAt=None; s.lastProbe=now
    def resume(s,now,boost):
        s.below=0; s.near=0; s.goodSince=now; s.lowSince=None; s.lastDown=False; s.maxSince=None
        if (not s.started) or now<s.futileUntil or s.unreach or boost<=0: return s.W
        base=s.resumeTo if s.resumeTo is not None else s.W
        b=min(s.max,base+boost)
        if b<=s.W: return s.W
        s.resumeTo=base; s.resumeAt=now; s.W=b; s.lastChange=now; s.settled=False
        return s.W
    @property
    def pending(s): return s.resumeTo is not None
    @property
    def baseline(s): return s.resumeTo if s.resumeTo is not None else s.W
    def drop(s,now):
        if s.resumeTo is None: return None
        back=s.resumeTo; s.resumeTo=None
        if back>=s.W: return None
        s.W=back; s.lastChange=now; s.lastDown=False; return s.W
    def raiseFloor(s,floor,now,confirmed):
        floor=max(s.min,min(s.max,floor)); same=floor==s.floor
        s.floorHold=min(C.MaxFloorHold,s.floorHold*2) if same else C.FloorHold
        s.floorConfirmed=confirmed or (same and s.floorConfirmed)
        s.floor=floor; s.floorUntil=now+s.floorHold; s.floorLearned=False
    def on(s,fps,now):
        if not s.started:
            s.started=True; s.goodSince=now; s.lastChange=now
            if s.learned:
                s.floor=s.W; s.floorUntil=now+C.LearnedHold; s.floorConfirmed=True; s.floorLearned=True; s.learnedCheck=True
        if math.isnan(fps): return 'None'
        if fps<C.IgnoreBelowFps:
            if s.lowSince is None: s.lowSince=now
            if now-s.lowSince<C.IgnoreLow:
                s.below=0; s.near=0; s.goodSince=now; s.maxSince=None; return 'None'
        else: s.lowSince=None
        # Queda brusca (de dentro da meta para menos de 65% dela, de uma leitura para a outra):
        # é corte de cena, replay ou menu travado em 30, não falta de potência. Espera até 15 s.
        prev=s.prevFps; s.prevFps=fps
        if C.Drop:
            if prev is not None and prev>=s.eff*C.MeetRatio and fps<s.eff*0.65 and s.dropUntil<now:
                s.dropUntil=now+C.DropHold
            if now<s.dropUntil:
                if fps<s.eff*0.65:
                    s.below=0; s.near=0; return 'None'
                s.dropUntil=NEG
        s.note(fps,now)
        if s.unreach:
            changed=(fps>=s.target*C.MeetRatio and (not C.Gate or fps>=s.plateau*1.05)) or fps>s.plateau*1.20
            s.exitN=s.exitN+1 if changed else 0
            if s.exitN>=2 or (now>=s.unreachUntil and s.probeAt is None):
                if s.exitN>=2: s.futileHold=C.FutileHold
                else: s.curve={}  # nova tentativa: mede de novo em vez de confiar no que já se viu
                s.unreach=False; s.eff=float(s.target); s.futileUntil=NEG; s.exitN=0; s.probeAt=None
        # Meta fora de alcance: a cada minuto, um teste curto de +3 W. Se o FPS subir de verdade,
        # a cena mudou (agora a potência ajuda) e o controle volta a buscar a meta.
        if C.Probe and s.unreach and fps>=C.IgnoreBelowFps:
            if s.probeAt is not None:
                if now-s.probeAt>=C.CurveSettle: s.probeV.append(fps)
                if len(s.probeV)>=3:
                    gain=sorted(s.probeV)[1]/s.probeBase if s.probeBase>0 else 1
                    s.probeAt=None; s.lastProbe=now
                    if s.probeDir<0:
                        if gain>=0.96:
                            s.lastProbe=now-C.ProbeEvery+20
                            return 'None'
                        s.downBlockUntil=now+180
                        s.W=min(s.max,s.W+C.StepUp); s.lastChange=now; s.lastDown=False; return 'Up'
                    if gain>=C.ProbeGain:
                        s.unreach=False; s.eff=float(s.target); s.futileUntil=NEG; s.exitN=0; s.probeAt=None; s.curve={}
                    else:
                        s.W=max(s.min,s.W-C.StepUp); s.lastChange=now; s.lastDown=False; return 'Down'
                else: return 'None'
            else:
                s.upN=s.upN+1 if fps>=s.plateau*1.05 else 0
            if s.probeAt is None and (now-s.lastProbe>=C.ProbeEvery or (s.upN>=3 and now-s.lastProbe>=10)) and now-s.lastChange>=10 and s.resumeTo is None:
                m,n=s.cavg(s.W,s.W,now)
                if n>=2:
                    down=C.DownProbe and s.proven is not None and s.W-C.StepUp>=s.proven and now>=s.downBlockUntil and s.upN<3
                    s.probeAt=now; s.probeBase=m; s.probeV=[]; s.probeDir=-1 if down else 1
                    if down:
                        s.W-=C.StepUp; s.lastChange=now; s.lastDown=False; return 'Down'
                    if s.W+C.StepUp>s.max: s.probeAt=None; return 'None'
                    s.W+=C.StepUp; s.lastChange=now; s.lastDown=False; return 'Up'
        below=fps<s.eff*C.BelowRatio
        near=(not below) and fps<s.eff*C.MeetRatio
        if s.resumeTo is not None and (not below) and now-s.resumeAt>=C.ResumeHold:
            back=s.resumeTo; s.resumeTo=None
            if back<s.W:
                s.W=back; s.lastChange=now; s.lastDown=False; return 'Down'
        if below or near:
            s.goodSince=now; s.settled=False
            if now<s.futileUntil:
                if not (s.lastDown and now-s.lastChange<=30) and abs(fps-s.futileFps)<=0.10*s.futileFps: return 'None'
                s.futileUntil=NEG
            if below: s.below+=1; s.near=0; enough=s.below>=2
            else: s.near+=1; s.below=0; enough=s.near>=C.NearN
            if s.climbW is not None: s.best=max(s.best,fps)
            if (not enough) or now-s.lastChange<C.UpCd: return 'None'
            if s.unreach and below:
                s.unreach=False; s.eff=float(s.target); s.futileUntil=NEG; s.exitN=0; s.probeAt=None
            s.below=0; s.near=0
            if C.Gate and s.W<s.max:
                cur,cn=s.cavg(s.W-1,s.W+1,now)
                hm,hn=s.cavg(s.W+3,s.max,now)
                hiBest=hm if hn>=C.CurveMinN else None
                if cn>=C.CurveMinN and hiBest is not None and hiBest<max(cur,fps)*C.FutileGain and hiBest>=max(cur,fps)*(1-C.SceneDev) and hiBest<s.target*C.MeetRatio:
                    plateau=max(cur,hiBest)
                    k=s.knee(plateau,now)
                    s.resumeTo=None; s.retClimb=None
                    if s.unreach: return 'None'   # já segurando: não estica o prazo da nova tentativa
                    s.mark(plateau,fps,now)
                    if k is not None and k<s.W:
                        s.W=max(s.min,k); s.lastChange=now; s.lastDown=False; return 'Down'
                    return 'None'
            ref=None
            if s.climbW is not None and s.W>s.climbW:
                cands=[h for h in s.hist if h[0]<=s.W-C.FutileClimb]
                if cands: ref=max(cands,key=lambda h:h[0])
                elif s.W>=s.max: ref=s.hist[0] if s.hist else None
            if ref is not None and fps<ref[1]*C.FutileGain:
                # joelho: menor potência da subida que já dava (quase) este FPS
                knee=min([h[0] for h in s.hist if h[1]>=fps*0.95]+[s.W])
                back=max(s.climbW,knee)
                if s.retClimb is not None and s.retClimb>back:
                    back=min(s.W,s.retClimb)
                elif back==s.climbW:
                    s.floor=s.climbFloor; s.floorUntil=s.climbFloorUntil; s.floorConfirmed=s.climbFloorConfirmed; s.floorLearned=s.climbFloorLearned
                if C.Gate:
                    k=s.knee(max(s.best,fps),now)
                    if k is not None and k<back: back=max(s.min,min(back,max(k,s.proven if s.proven is not None else s.min)))
                s.W=back; s.resumeTo=None; s.retClimb=None
                s.coarse=s.climbCoarse; s.climbW=None
                s.unreach=True; s.unreachUntil=now+s.futileHold; s.unreachSince=now
                s.plateau=max(s.best,fps); s.eff=max(15.0,min(float(s.target),s.plateau*0.97))
                s.futileFps=fps; s.futileUntil=now+s.futileHold; s.futileHold=min(C.UnreachMaxHold,s.futileHold*2)
                s.lastChange=now; s.lastDown=False; s.maxSince=None; return 'Down'
            if s.W>=s.max:
                # No teto sem alcançar a meta (e sem subida para comparar): depois de 20 s, meta fora de alcance.
                if s.maxSince is None: s.maxSince=now; s.best=fps
                s.best=max(s.best,fps)
                if now-s.maxSince>=20:
                    s.maxSince=None; s.climbW=None
                    s.unreach=True; s.unreachUntil=now+s.futileHold; s.unreachSince=now
                    s.plateau=s.best; s.eff=max(15.0,min(float(s.target),s.plateau*0.97))
                    s.futileFps=fps; s.futileUntil=now+s.futileHold; s.futileHold=min(C.UnreachMaxHold,s.futileHold*2)
                return 'None'
            if s.climbW is None:
                s.climbW=s.resumeTo if s.resumeTo is not None else s.W
                s.best=fps; s.hist=[]
                s.climbFps=fps; s.climbFloor=s.floor; s.climbFloorUntil=s.floorUntil
                s.climbFloorConfirmed=s.floorConfirmed; s.climbCoarse=s.coarse; s.climbFloorLearned=s.floorLearned
            s.resumeTo=None
            afterProbe=s.lastDown and now-s.lastChange<=30 and not (C.RetFix and getattr(s,'lastDownWasReturn',False))
            if afterProbe:
                s.coarse=False; s.retClimb=None; s.climbFromProbe=True
                s.probeWasReturn=getattr(s,'lastDownWasReturn',False)
                s.preFloor=(s.floor,s.floorUntil,s.floorConfirmed,s.floorHold,s.floorLearned)
                s.probeFrom=s.W+(s.lastDownStep if not s.probeWasReturn else 1)
                s.raiseFloor(s.W+1,now,True)
                s.retClimb=None if s.probeWasReturn else min(s.max,s.probeFrom)
            elif s.climbFromProbe and below and not getattr(s,'probeWasReturn',False) and s.W>=s.floor and getattr(s,'preFloor',None):
                s.floor,s.floorUntil,s.floorConfirmed,s.floorHold,s.floorLearned=s.preFloor; s.preFloor=None
                s.climbFromProbe=False
            elif (not s.climbFromProbe) and s.climbW is not None and s.proven==s.climbW:
                if s.retClimb is None: s.retClimb=s.climbW
            s.hist.append((s.W,fps))
            s.W=min(s.max,s.W+(C.StepUp if below else 1))
            s.lastChange=now; s.lastDown=False; return 'Up'
        s.below=0; s.near=0; s.climbW=None; s.climbFromProbe=False; s.maxSince=None
        if not s.unreach: s.futileHold=C.FutileHold
        if now-max(s.goodSince,s.lastChange)>=C.Proven: s.proven=s.W
        if now-s.goodSince>=C.DownAfter: s.futileUntil=NEG
        surplus=fps>=s.eff*C.Surplus
        lb=max(s.min,s.floor) if (now<s.floorUntil and not (surplus and s.floorLearned)) else s.min
        fast=s.coarse or surplus
        da=C.CoarseDownAfter if fast else C.DownAfter
        if s.unreach: return 'None'
        if s.retClimb is not None:
            if now-s.goodSince<C.ReturnAfter or now-s.lastChange<C.ReturnAfter: return 'None'
            base=s.retClimb; s.retClimb=None
            to=max(lb,base)
            if to<s.W:
                s.W=to; s.lastChange=now; s.lastDown=True; s.lastDownWasReturn=True; s.settled=False; return 'Down'
        if s.W>lb and now-s.goodSince>=da and now-s.lastChange>=da:
            w0=s.W
            big=fast and not ((not s.coarse) and s.floorConfirmed and (not s.floorLearned) and s.W-C.CoarseStep<s.floor) and not s.learnedCheck
            s.W=max(lb,s.W-(C.CoarseStep if big else C.StepDown)); s.lastDownStep=w0-s.W; s.lastDownWasReturn=False; s.learnedCheck=False
            s.lastChange=now; s.lastDown=True; s.settled=False; return 'Down'
        if s.unreach: pass
        elif s.W<=lb and now-s.lastChange>=C.Settle and (s.W==s.min or s.floorConfirmed): s.settled=True
        elif C.NewSettle and s.W<=lb and s.floorConfirmed and (not s.floorLearned) and now<s.floorUntil and now-max(s.goodSince,s.lastChange)>=12: s.settled=True
        return 'None'
