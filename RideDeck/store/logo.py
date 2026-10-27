"""RideDeck logo: a speedometer gauge over a sound wave, on a 108-unit adaptive-icon grid."""
import math, sys
cx,cy,r,sw=54,50,24,8
def pt(a,rr=r):
    t=math.radians(a); return cx+rr*math.cos(t), cy+rr*math.sin(t)
f=lambda p:f"{p[0]:.2f},{p[1]:.2f}"
s=pt(135); e=pt(45); p=pt(318)
track=f"M{f(s)} A{r},{r} 0 1,1 {f(e)}"
prog=f"M{f(s)} A{r},{r} 0 1,1 {f(p)}"
a=318; tip=pt(a,20); l=pt(a+90,3.6); rg=pt(a-90,3.6); back=pt(a+180,5)
needle=f"M{f(l)} L{f(tip)} L{f(rg)} L{f(back)} Z"
ticks=[f"M{f(pt(an,r-8.5))} L{f(pt(an,r-11.5))}" for an in range(135,406,45)]
bars=[]; xs=[43,48.5,54,59.5,65]; hs=[4.5,10,13,8,4]; mid=76.5
for x,h in zip(xs,hs): bars.append((x-1.8,mid-h/2,3.6,h))
def rr(x,y,w,h):  # rounded rect path, radius w/2
    rad=w/2
    return (f"M{x+rad:.2f},{y:.2f} L{x+w-rad:.2f},{y:.2f} A{rad},{rad} 0 0,1 {x+w:.2f},{y+rad:.2f} "
            f"L{x+w:.2f},{y+h-rad:.2f} A{rad},{rad} 0 0,1 {x+w-rad:.2f},{y+h:.2f} L{x+rad:.2f},{y+h:.2f} "
            f"A{rad},{rad} 0 0,1 {x:.2f},{y+h-rad:.2f} L{x:.2f},{y+rad:.2f} A{rad},{rad} 0 0,1 {x+rad:.2f},{y:.2f} Z")
barpath=" ".join(rr(*b) for b in bars)
hub=f"M{cx-4.6},{cy} a4.6,4.6 0 1,0 9.2,0 a4.6,4.6 0 1,0 -9.2,0"
dot=f"M{cx-1.9},{cy} a1.9,1.9 0 1,0 3.8,0 a1.9,1.9 0 1,0 -3.8,0"
G=dict(track=track,prog=prog,needle=needle,ticks=ticks,bars=barpath,hub=hub,dot=dot,sw=sw)

def svg(size=432, bg=True, safe=False, glow=True):
    t=''.join(f'<path d="{x}" stroke="#FFFFFF" stroke-opacity="0.55" stroke-width="1.8" stroke-linecap="round"/>' for x in ticks)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 108 108">
<defs>
<radialGradient id="bg" cx="50%" cy="38%" r="80%"><stop offset="0" stop-color="#0C3352"/><stop offset="0.6" stop-color="#041223"/><stop offset="1" stop-color="#010409"/></radialGradient>
<linearGradient id="g" x1="30" y1="80" x2="78" y2="28" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#03B7FF"/><stop offset="1" stop-color="#8CF7FF"/></linearGradient>
<filter id="glow" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="2.2"/></filter>
</defs>
{'<rect width="108" height="108" fill="url(#bg)"/>' if bg else ''}
<path d="{track}" stroke="#FFFFFF" stroke-opacity="0.13" stroke-width="{sw}" stroke-linecap="round" fill="none"/>
{f'<path d="{prog}" stroke="#03B7FF" stroke-opacity="0.7" stroke-width="{sw}" stroke-linecap="round" fill="none" filter="url(#glow)"/>' if glow else ''}
<path d="{prog}" stroke="url(#g)" stroke-width="{sw}" stroke-linecap="round" fill="none"/>
{t}
<path d="{needle}" fill="#FFFFFF"/>
<path d="{hub}" fill="#FFFFFF"/><path d="{dot}" fill="#03B7FF"/>
<path d="{barpath}" fill="url(#g)"/>
{'<circle cx="54" cy="54" r="33" fill="none" stroke="red" stroke-width="0.3" stroke-dasharray="1 1"/>' if safe else ''}
</svg>'''
lin='''<aapt:attr name="android:{attr}"><gradient android:type="linear" android:startX="30" android:startY="80" android:endX="78" android:endY="28" android:startColor="#03B7FF" android:endColor="#8CF7FF"/></aapt:attr>'''
head='''<?xml version="1.0" encoding="utf-8"?>
<!-- {c} -->
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:aapt="http://schemas.android.com/aapt"
    android:width="108dp"
    android:height="108dp"
    android:viewportWidth="108"
    android:viewportHeight="108">
'''
def fg(mono=False):
    o=head.format(c="RideDeck logo: a speedometer gauge over a sound wave. Generated with store/logo.py; edit there." if not mono else "Single-colour RideDeck logo for themed (Material You) icons.")
    o+=f'''    <path android:pathData="{track}" android:strokeColor="#FFFFFF" android:strokeAlpha="{0.3 if mono else 0.13}" android:strokeWidth="{sw}" android:strokeLineCap="round" android:fillColor="#00000000" />\n'''
    if mono:
        o+=f'''    <path android:pathData="{prog}" android:strokeColor="#FFFFFF" android:strokeWidth="{sw}" android:strokeLineCap="round" android:fillColor="#00000000" />\n'''
    else:
        o+=f'''    <path android:pathData="{prog}" android:strokeWidth="{sw}" android:strokeLineCap="round" android:fillColor="#00000000">\n        {lin.format(attr="strokeColor")}\n    </path>\n'''
    for t in ticks:
        o+=f'''    <path android:pathData="{t}" android:strokeColor="#FFFFFF" android:strokeAlpha="0.55" android:strokeWidth="1.8" android:strokeLineCap="round" />\n'''
    o+=f'''    <path android:pathData="{needle}" android:fillColor="#FFFFFF" />\n    <path android:pathData="{hub}" android:fillColor="#FFFFFF" />\n'''
    if not mono: o+=f'''    <path android:pathData="{dot}" android:fillColor="#03B7FF" />\n'''
    if mono:
        o+=f'''    <path android:pathData="{barpath}" android:fillColor="#FFFFFF" />\n'''
    else:
        o+=f'''    <path android:pathData="{barpath}">\n        {lin.format(attr="fillColor")}\n    </path>\n'''
    return o+'</vector>\n'
bg=head.format(c="Deep navy launcher background with a soft glow behind the gauge.")+'''    <path android:pathData="M0,0h108v108h-108z">
        <aapt:attr name="android:fillColor">
            <gradient android:type="radial" android:centerX="54" android:centerY="41" android:gradientRadius="86">
                <item android:offset="0" android:color="#FF0C3352" />
                <item android:offset="0.6" android:color="#FF041223" />
                <item android:offset="1" android:color="#FF010409" />
            </gradient>
        </aapt:attr>
    </path>
</vector>
'''

if __name__ == "__main__":
    # Regenerates the launcher icon vectors and the SVG logo. Render the PNGs for the
    # Play Store (icon-512.png, feature-graphic.png) from the SVGs with any browser.
    import os
    here = os.path.dirname(os.path.abspath(__file__))
    res = os.path.join(here, "..", "app", "src", "main", "res", "drawable")
    open(os.path.join(res, "ic_launcher_foreground.xml"), "w").write(fg())
    open(os.path.join(res, "ic_launcher_monochrome.xml"), "w").write(fg(True))
    open(os.path.join(res, "ic_launcher_background.xml"), "w").write(bg)
    open(os.path.join(here, "logo.svg"), "w").write(svg(512))
