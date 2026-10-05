"""Editable vector/native Figma design source, built from the implemented WPF hierarchy.

The native plugin is a delivery fallback for the MCP account limit, not evidence of
a cloud write. Pillow measures the installed Windows font and renders local review images;
screenshots are never embedded into any design layer.
"""
from pathlib import Path
from copy import deepcopy
from html import escape
import json
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'docs/design/resource-transfer-20261005'
OUT.mkdir(parents=True, exist_ok=True)
TOKENS = {
    'Light': dict(Window='#F5F5F7', Surface='#FFFFFF', Text='#1D1D1F', Muted='#646469', Border='#DFDFE3', Accent='#1D1D1F', AccentSoft='#EBEBEF', Error='#B42318'),
    'Dark': dict(Window='#161618', Surface='#222225', Text='#F5F5F7', Muted='#B1B1B8', Border='#3A3A3F', Accent='#F5F5F7', AccentSoft='#38383C', Error='#FF9C94'),
}

def t(text, size=14, role='Text', bold=False):
    if text.lstrip().startswith(('☑ ', '□ ')):
        clean=text.lstrip();checked=clean.startswith('☑')
        control=row([dict(kind='checkbox',checked=checked,width=18,height=20,name='Selected' if checked else 'Unselected'),t(clean[2:],size,role,bold)],gap=8)
        control['width']=26+ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',size).getlength(clean[2:])+6
        return control
    return dict(kind='text', text=text, size=size, role=role, bold=bold)

def btn(text, style='secondary', width=None):
    return dict(kind='button', text=text, style=style, width=width or max(86, len(text)*14+28), height=38)

def col(children, gap=12, pad=0, fill=None, border=False, width=None, height=None, name='Group'):
    return dict(kind='column', children=children, gap=gap, pad=pad, fill=fill, border=border, width=width, height=height, name=name)

def row(children, gap=12, pad=0, fill=None, height=None, name='Row'):
    return dict(kind='row', children=children, gap=gap, pad=pad, fill=fill, height=height, name=name)

def card(children, height=None, width=None, name='Card'):
    return col(children, pad=20, fill='Surface', border=True, height=height, width=width, name=name)

def spacer(height=8, width=None):
    return dict(kind='space', height=height, width=width, name='Spacing')

def tabs(current='工作流'):
    return row([t(('— ' if label==current else '')+label, bold=label==current) for label in ['工作流','模型','节点','导出打包']], gap=28, height=42, name='Resource tabs')

def title(text, description=None):
    items=[t(text,30,bold=True)]
    if description: items.append(t(description,13,'Muted'))
    return col(items,gap=8,name='Page heading')

def panelrows(lines):
    return [t(line,14,'Muted' if line.startswith('  ') else 'Text') for line in lines]

def home(state=None):
    name='尚未选择实例' if state=='empty' else 'ComfyUI Desktop · 示例实例'
    instance=card([
        row([t('当前实例',17,bold=True),spacer(),btn('重新扫描','home'),btn('关联目录','home')]),
        t(name,bold=True),t('ComfyUI Core  未确认     Desktop  未确认',13,'Muted'),
        row([btn('工作流文件夹','home'),btn('模型文件夹','home'),btn('节点文件夹','home')],gap=8),
        t('›  版本详情与资源目录',13),
    ],height=286,name='Home.Instance')
    actions=card([row([
        col([t('导入与安装',17,bold=True),t('检查资源，再安装到实例。',13,'Muted'),spacer(6),btn('导入资源','home')],gap=10),
        col([t('管理与分享',17,bold=True),t('选择资源，导出为资源包。',13,'Muted'),spacer(6),btn('导出打包','home')],gap=10),
    ],gap=32)],height=158,name='Home.Shortcuts')
    status = '尚未选择实例' if state=='empty' else '可查看资源'
    hint='在顶部选择实例，或点击“关联目录”。' if state=='empty' else '资源来自当前所选实例。'
    if state=='offline': status='离线可用';hint='尚未核对前端列表；保留磁盘资源。'
    if state=='error': status='操作未完成';hint='服务查询失败，保留当前扫描结果。'
    right=card([t('当前状态',17,bold=True),spacer(4),t(status,16,bold=True),t(hint,13,'Muted'),spacer(12),
        t('工作流                                 —',13,'Muted'),t('模型                                    —',13,'Muted'),t('节点包                                 —',13,'Muted'),
        spacer(6),t('›  查看详情',13)],height=460,width=300,name='Home.Status')
    return col([title('让 ComfyUI 资源井然有序','管理所选实例的资源，或导入资源包检查并安装。'),
        row([col([instance,actions],gap=16),right],gap=24)],gap=24)

def library(state=None):
    lines=['▾ 工作流','  ▾ 图像','    □ 人像修复.json','    □ 放大图片.json','  ▾ 视频','    □ 动作迁移.json']
    if state=='empty': lines=['没有匹配资源。','请清空搜索，或在首页扫描实例。']
    tree=card(panelrows(lines),height=330,name='Workflow directory tree')
    deps=col([t('人像修复',17,bold=True),t('勾选依赖决定实际导出内容。',13,'Muted'),t('›  实际位置',13),
        t('关联模型',17,bold=True),t('□ 修复模型.safetensors',bold=True),t('可用 · checkpoints/修复模型.safetensors',13,'Muted'),t('用于：人像修复、放大图片',13,'Muted'),
        t('关联节点包',17,bold=True),t('□ 示例节点包 · RestoreImage、LoadImage',bold=True),t('加载状态待验证 · custom_nodes/示例节点包',13,'Muted'),
        t('内置节点 · 无需迁移',13,'Muted')],gap=10,name='Dependencies grouped by package')
    notice='已核对所选实例的前端保存列表'
    if state in ('offline','error'): notice='尚未核对前端列表'+('：服务查询失败。' if state=='error' else '')
    disk=t('›  仅在磁盘发现的工作流',bold=True) if state not in ('offline','error') else t('离线时继续显示在目录树中，不归入磁盘独立区。',13,'Muted')
    return col([row([title('资源库'),card([t('搜索资源',13,'Muted')],width=230,name='Search')]),tabs(),
        row([t('按实例目录选择资源',13,'Muted'),btn('导出勾选资源')]),row([tree,deps],gap=20),t(notice,13,'Muted'),disk],gap=16)

def model_library():
    base=library()
    base['children'][1]=tabs('模型')
    base['children'][3]['children'][0]=card(panelrows(['▾ 实例模型 · models','  ▾ checkpoints','    □ 修复模型.safetensors','  ▾ loras','    □ 风格.safetensors','▾ 共享 / 扩展模型 · 示例共享目录','  ▾ diffusion_models','    □ 示例模型.safetensors']),height=330,name='Model roots')
    base['children'][3]['children'][1]=col([t('修复模型.safetensors',17,bold=True),t('按原目录保留模型及配套文件。',13,'Muted'),t('›  实际位置',13),t('用于：人像修复、放大图片',13,'Muted')],gap=12)
    base['children']=base['children'][:-2]
    return base

def node_library():
    base=library()
    base['children'][1]=tabs('节点')
    base['children'][3]['children'][0]=card(panelrows(['▾ 节点包 · custom_nodes','  ▾ □ 示例节点包','    RestoreImage','    LoadImage','  ▾ □ 另一个节点包','    ResizeImage']),height=330,name='Node package hierarchy')
    base['children'][3]['children'][1]=col([t('示例节点包',17,bold=True),t('完整节点包 · 已识别的节点类型归入包内。',13,'Muted'),t('加载状态以运行实例核对为准。',13,'Muted'),t('›  实际位置',13),t('用于：人像修复、放大图片',13,'Muted')],gap=12)
    base['children']=base['children'][:-2]
    return base

def export_page():
    references=col([t('用于分析的工作流',17,bold=True),t('参考不代表写入；“写入”单独勾选。',13,'Muted'),
        card([t('☑ 人像修复.json',bold=True),t('□ 写入工作流文件',13),t('☑ 放大图片.json',bold=True),t('□ 写入工作流文件',13)],height=160),btn('分析并勾选依赖')],width=260)
    dep=col([t('2 个参考工作流的依赖',17,bold=True),t('已勾选可唯一确认的依赖，可取消不需要的项目。',13,'Muted'),
        t('关联模型',17,bold=True),t('□ 修复模型.safetensors'),t('缺失 · 未选择，不阻止节点包导出',13,'Muted'),
        t('关联节点包',17,bold=True),t('☑ 示例节点包 · RestoreImage、LoadImage',bold=True),t('用于：人像修复、放大图片',13,'Muted'),t('完整节点包 · 共享依赖只写入一次',13,'Muted')],gap=10)
    return col([tabs('导出打包'),card([
        row([t('导出打包',17,bold=True),spacer(),btn('选择模型'),btn('选择节点')]),
        row([t('实际写入 ZIP：'),t('□ 工作流文件'),t('□ 模型'),t('☑ 节点包')],gap=16),row([references,dep],gap=20),
        card([t('ZIP 内路径                                        大小（字节）',13,'Muted'),t('custom_nodes/示例节点包/__init__.py',13),t('custom_nodes/示例节点包/requirements.txt',13)],height=100,name='ZIP preview'),
        row([col([t('仅导出已勾选的节点包。',13),t('›  高级选项',13)],gap=6),btn('生成导出预览'),btn('保存 ZIP','primary')]),
    ],name='Independent export',height=560)],gap=12)

def import_page(state=None):
    source=card([t('导入资源',17,bold=True),t('选择 ZIP、工作流、资源目录或旧格式资源包。',13,'Muted'),
        row([btn('选择文件'),btn('选择目录')]),t('也可以将文件或目录拖到窗口中。',13,'Muted'),
        t('来源：'+('尚未选择来源' if state=='empty' else '示例节点资源.zip'),13,'Muted')],height=180,name='Import.Source')
    contents=card([t('待安装内容',17,bold=True),t('节点包 / 示例节点包',bold=True),t('☑ __init__.py',13),t('☑ requirements.txt',13),t('›  来源与校验',13),t('›  依赖检查',13)],height=220)
    if state=='empty': contents=card([t('选择来源后显示分组内容与依赖检查。',13,'Muted')],height=220)
    if state=='error': contents=card([t('来源核对失败',17,bold=True),t('资源清单校验失败，未生成安装计划。',13,'Error'),t('请重新选择完整资源包。',13,'Muted')],height=220)
    summary=card([t('安装摘要',17,bold=True),t('目标：示例实例',13),t('工作流：不安装',13,'Muted'),t('模型：不安装',13,'Muted'),t('节点包：示例节点包',13),spacer(12),t('先检查计划，再安装。',13,'Muted'),btn('生成安装预览'),btn('确认并安装','primary' if not state else 'disabled')],width=280,height=400,name='Fixed install summary')
    return col([title('导入安装','先检查资源内容与依赖，再选择安装到当前实例。'),row([col([source,contents,t('›  导入历史 · 重新核对使用相同流程',13)],gap=16),summary],gap=24)],gap=24)

def settings(about=False):
    nav=row([t(label,14,bold=label==('关于作者' if about else '日志')) for label in ['界面偏好','Desktop 实例','资源库','日志','关于作者']],gap=26,height=48)
    if about:
        content=card([t('关于作者',17,bold=True),t('LightyearXizIl',bold=True),t('GitHub · https://github.com/LightyearXizIl/ComfyUI-FlowPack',13),btn('复制链接'),spacer(12),t('软件更新',17,bold=True),t('当前版本：以软件显示为准',13,'Muted'),btn('检查更新'),t('发现更新后，同一按钮切换为下载并安装。',13,'Muted')],height=340)
    else:
        content=card([t('日志',17,bold=True),t('记录强度',bold=True),t('关闭   /   仅错误   /   标准   /   详细',13,'Muted'),t('保留时间',bold=True),t('7 天   /   10 天   /   15 天   /   30 天',13,'Muted'),t('定期清理超期日志，一键清理只删除日志文件。',13,'Muted'),row([btn('打开日志目录'),btn('一键清理日志')])],height=340)
    return col([title('设置'),nav,content],gap=16)

def header(selected='Home'):
    brand=col([t('FlowPack',20,bold=True),t('COMFYUI RESOURCE TOOL',10,'Muted')],gap=0,width=180)
    nav=row([t(label,14,bold=key==selected) for key,label in [('Home','首页'),('Library','资源库'),('Import','导入安装')]],gap=28);nav['align']='CENTER'
    brand['width']=496; nav['width']=240
    controls=row([spacer(width=310),btn('选择实例'),t('设置',14)],gap=16);controls['width']=496
    result=row([brand,nav,controls],gap=0,pad=24,height=64,fill='Window',name='App navigation');result['padY']=12
    return result

def screen(name,theme,body,selected='Home',progress=None):
    children=[header(selected)]
    if progress:
        children.append(col([t(progress,13),dict(kind='bar',height=3,width=None,name='Stage progress')],gap=6,pad=12,fill='Surface',name='Global progress'))
    children.append(col([body],pad=32,name='PageHost'))
    return dict(name=name,theme=theme,root=col(children,gap=0,fill='Window',width=1280,height=800,name=name))

SCREENS=[]
for theme in TOKENS:
    for name,body,selected in [('首页',home(),'Home'),('资源库-工作流',library(),'Library'),('资源库-模型',model_library(),'Library'),('资源库-节点包',node_library(),'Library'),('独立导出-仅节点',export_page(),'Library'),('统一导入',import_page(),'Import'),('设置-日志',settings(),'Settings'),('设置-关于作者与更新',settings(True),'Settings')]:
        SCREENS.append(screen(name,theme,body,selected))
SCREENS += [screen('加载-已知当前阶段','Light',home(),'Home','扫描实例 · 识别工作流 · 42%'),
    screen('加载-未知总量','Dark',home(),'Home','扫描实例 · 发现资源文件 · 已处理 120 文件（活动进度）'),
    screen('离线-列表未核对','Light',library('offline'),'Library'),screen('空内容-导入','Light',import_page('empty'),'Import'),
    screen('错误-前端查询失败','Dark',library('error'),'Library'),screen('错误-资源校验失败','Light',import_page('error'),'Import')]

def wrap_text(text,width,size):
    # Measure CJK text using the actual Windows fallback font, keeping editable text in SVG/Figma.
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',size)
    lines=[]
    for paragraph in text.split('\n'):
        current=''
        for char in paragraph:
            if current and font.getlength(current+char)>width: lines.append(current);current=''
            current+=char
        lines.append(current)
    return lines or ['']

def measure(node,width):
    node['w']=node.get('width') or width
    w=node['w'];kind=node['kind'];pad=node.get('pad',0);padY=node.get('padY',pad);gap=node.get('gap',0)
    if kind=='text':
        node['lines']=wrap_text(node['text'],w,node['size']);node['h']=len(node['lines'])*(node['size']*1.45)
    elif kind in ('space','bar','button','checkbox'): node['h']=node.get('height',8)
    else:
        items=node['children'];inner=w-pad*2
        if kind=='row':
            def text_width(n): return ImageFont.truetype('C:/Windows/Fonts/'+('msyhbd.ttc' if n.get('bold') else 'msyh.ttc'),n.get('size',14)).getlength(n.get('text',''))+4
            fixed=sum(x.get('width') or (text_width(x) if x['kind']=='text' else 0) for x in items)
            flex=[x for x in items if not x.get('width') and x['kind']!='text']
            share=max(1,(inner-gap*max(0,len(items)-1)-fixed)/max(1,len(flex)))
            for child in items: measure(child,child.get('width') or (text_width(child) if child['kind']=='text' else share))
            content=max((x['h'] for x in items),default=0)
        else:
            for child in items: measure(child,inner)
            content=sum(x['h'] for x in items)+gap*max(0,len(items)-1)
        node['h']=node.get('height') or content+padY*2
        # Screens and intentionally tall cards may have free space; never clip a text layer.
        if node.get('height') and node['name']!='App navigation': node['h']=max(node['h'],content+padY*2)
    return node

def render(node,theme,x=0,y=0):
    kind=node['kind'];w=node['w'];h=node['h'];p=TOKENS[theme];bits=[]
    if node.get('fill') or node.get('border'):
        fill=p.get(node.get('fill'),'none');border=p['Border'] if node.get('border') else 'none'
        bits.append(f'<rect x="{x:.2f}" y="{y:.2f}" width="{w:.2f}" height="{h:.2f}" rx="{12 if node.get("border") else 0}" fill="{fill}" stroke="{border}"/>')
    if kind=='text':
        for i,line in enumerate(node['lines']): bits.append(f'<text x="{x:.2f}" y="{y+node["size"]+i*node["size"]*1.45:.2f}" font-size="{node["size"]}" font-weight="{600 if node["bold"] else 400}" fill="{p[node["role"]]}">{escape(line)}</text>')
    elif kind=='button':
        bg='#FFFFFF' if node['style']=='home' else p['Accent'] if node['style']=='primary' else p['Surface']
        fg='#1D1D1F' if node['style']=='home' else p['Window'] if node['style']=='primary' else p['Text']
        opacity='.45' if node['style']=='disabled' else '1'
        bits.append(f'<g opacity="{opacity}"><rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="{bg}" stroke="{p["Border"]}"/><text x="{x+w/2}" y="{y+24}" text-anchor="middle" font-size="14" font-weight="600" fill="{fg}">{escape(node["text"])}</text></g>')
    elif kind=='bar': bits.append(f'<rect x="{x}" y="{y}" width="{w}" height="3" fill="{p["Border"]}"/><rect x="{x}" y="{y}" width="{w*.42}" height="3" fill="{p["Accent"]}"/>')
    elif kind=='checkbox':
        bits.append(f'<rect x="{x}" y="{y+1}" width="18" height="18" rx="4" fill="{p["Accent"] if node["checked"] else p["Surface"]}" stroke="{p["Muted"]}"/>')
        if node['checked']:bits.append(f'<path d="M {x+3},{y+10} L {x+7},{y+14} L {x+15},{y+5}" fill="none" stroke="{p["Window"]}" stroke-width="2"/>')
    elif kind in ('row','column'):
        cx=x+node.get('pad',0);cy=y+node.get('padY',node.get('pad',0))
        if node.get('align')=='CENTER':cx+=(w-node.get('pad',0)*2-sum(c['w'] for c in node['children'])-node['gap']*(len(node['children'])-1))/2
        for child in node['children']:
            bits.append(render(child,theme,cx,cy))
            if kind=='row': cx+=child['w']+node['gap']
            else: cy+=child['h']+node['gap']
    return ''.join(bits)

for item in SCREENS:
    measure(item['root'],1280)
    svg=f'<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="{item["root"]["h"]:.0f}" viewBox="0 0 1280 {item["root"]["h"]:.0f}"><title>FlowPack 可编辑设计源：{escape(item["name"])} · {item["theme"]}</title><g font-family="Segoe UI,Microsoft YaHei UI,Microsoft YaHei,sans-serif">{render(item["root"],item["theme"])}</g></svg>'
    (OUT/(item['theme']+'-'+item['name']+'.svg')).write_text(svg,encoding='utf-8')

def raster_review(item):
    """Optional local inspection only; never embedded into the editable deliverable."""
    canvas=Image.new('RGB',(1280,800),TOKENS[item['theme']]['Window']);draw=ImageDraw.Draw(canvas)
    theme=TOKENS[item['theme']]
    def visit(n,x=0,y=0):
        kind=n['kind'];w=n['w'];h=n['h']
        if n.get('fill') or n.get('border'): draw.rounded_rectangle((x,y,x+w,y+h),radius=12 if n.get('border') else 0,fill=theme.get(n.get('fill')),outline=theme['Border'] if n.get('border') else None)
        if kind=='text':
            font=ImageFont.truetype('C:/Windows/Fonts/'+('msyhbd.ttc' if n['bold'] else 'msyh.ttc'),n['size'])
            for i,line in enumerate(n['lines']):draw.text((x,y+i*n['size']*1.45),line,fill=theme[n['role']],font=font)
        elif kind=='button':
            bg='#FFFFFF' if n['style']=='home' else theme['Accent'] if n['style']=='primary' else theme['Surface']
            fg='#1D1D1F' if n['style']=='home' else theme['Window'] if n['style']=='primary' else theme['Muted'] if n['style']=='disabled' else theme['Text']
            draw.rounded_rectangle((x,y,x+w,y+h),radius=8,fill=bg,outline=theme['Border']);font=ImageFont.truetype('C:/Windows/Fonts/msyhbd.ttc',14)
            draw.text((x+w/2,y+9),n['text'],anchor='mt',fill=fg,font=font)
        elif kind=='bar':draw.rectangle((x,y,x+w,y+3),fill=theme['Border']);draw.rectangle((x,y,x+w*.42,y+3),fill=theme['Accent'])
        elif kind=='checkbox':
            draw.rounded_rectangle((x,y+1,x+18,y+19),radius=4,fill=theme['Accent'] if n['checked'] else theme['Surface'],outline=theme['Muted'])
            if n['checked']:draw.line([(x+3,y+10),(x+7,y+14),(x+15,y+5)],fill=theme['Window'],width=2)
        cx=x+n.get('pad',0);cy=y+n.get('padY',n.get('pad',0))
        if n.get('align')=='CENTER':cx+=(w-n.get('pad',0)*2-sum(c['w'] for c in n['children'])-n['gap']*(len(n['children'])-1))/2
        for child in n.get('children',[]):
            visit(child,cx,cy)
            if kind=='row':cx+=child['w']+n['gap']
            else:cy+=child['h']+n['gap']
    visit(item['root'])
    review=ROOT/'artifacts/acceptance/resource-transfer-20261005/design-review';review.mkdir(exist_ok=True)
    canvas.save(review/(item['theme']+'-'+item['name']+'.png'))
for item in SCREENS:
    if item['theme']=='Light' and item['name'] in ['首页','独立导出-仅节点','统一导入']: raster_review(item)

payload=dict(pageName='FlowPack · 资源转移改版 · 2026-10-05',tokens=TOKENS,screens=SCREENS)
(OUT/'design-source.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2),encoding='utf-8')
PLUGIN=r'''
// Native Figma development plugin. Never executed by MCP in this task: account limit.
async function main() {
 const fonts=await figma.listAvailableFontsAsync();
 const font=fonts.find(f=>f.fontName.family==='Microsoft YaHei UI' && /regular|normal/i.test(f.fontName.style))?.fontName
   || fonts.find(f=>f.fontName.family==='Microsoft YaHei' && /regular|normal/i.test(f.fontName.style))?.fontName
   || fonts.find(f=>f.fontName.family==='Noto Sans SC' && /regular|normal/i.test(f.fontName.style))?.fontName;
 if (!font) throw new Error('请启用 Microsoft YaHei UI / Microsoft YaHei / Noto Sans SC 字体后重试。');
 await figma.loadFontAsync(font);
 const boldFont=fonts.find(f=>f.fontName.family===font.family && /bold/i.test(f.fontName.style))?.fontName||font;await figma.loadFontAsync(boldFont);
 const page=figma.createPage();page.name=DATA.pageName;await figma.setCurrentPageAsync(page);
 const collection=figma.variables.createVariableCollection('FlowPack / 资源转移改版');
 collection.renameMode(collection.defaultModeId,'Light');const darkMode=collection.addMode('Dark');
 const colorVars={};const rgb=h=>({r:parseInt(h.slice(1,3),16)/255,g:parseInt(h.slice(3,5),16)/255,b:parseInt(h.slice(5,7),16)/255});
 for (const role of Object.keys(DATA.tokens.Light)) {
   const v=figma.variables.createVariable(role,collection,'COLOR');
   v.setValueForMode(collection.defaultModeId,{...rgb(DATA.tokens.Light[role]),a:1});v.setValueForMode(darkMode,{...rgb(DATA.tokens.Dark[role]),a:1});colorVars[role]=v;
 }
 const paint=(theme,role)=>figma.variables.setBoundVariableForPaint({type:'SOLID',color:rgb(DATA.tokens[theme][role])},'color',colorVars[role]);
 const styles={};
 for(const size of [10,13,14,16,17,20,30]) {const st=figma.createTextStyle();st.name='FlowPack / '+size;st.fontName=font;st.fontSize=size;st.lineHeight={unit:'PERCENT',value:145};styles[size]=st;}
 const label=async (text,size,theme,role)=>{const n=figma.createText();n.fontName=font;n.fontSize=size;n.characters=text;n.fills=[paint(theme,role)];await n.setTextStyleIdAsync(styles[size].id);n.textAutoResize='HEIGHT';return n;};
 const buttons={};
 for(const theme of ['Light','Dark']) for(const style of ['home','secondary','primary','disabled']) {
   const c=figma.createComponent();c.name='Theme='+theme+', Style='+style;c.resize(140,38);c.layoutMode='HORIZONTAL';c.primaryAxisAlignItems='CENTER';c.counterAxisAlignItems='CENTER';c.primaryAxisSizingMode='FIXED';c.counterAxisSizingMode='FIXED';c.cornerRadius=8;
   c.fills=style==='home'?[{type:'SOLID',color:rgb('#FFFFFF')}]:[paint(theme,style==='primary'?'Accent':'Surface')];c.strokes=[paint(theme,'Border')];
   const tx=await label('按钮',14,theme,style==='primary'?'Window':'Text');if(style==='home')tx.fills=[{type:'SOLID',color:rgb('#1D1D1F')}];c.appendChild(tx);if(style==='disabled')c.opacity=.45;
   page.appendChild(c);buttons[theme+'/'+style]=c;
 }
 const set=figma.combineAsVariants(Object.values(buttons),page);set.name='FlowPack / Button';set.x=0;set.y=-250;set.layoutMode='HORIZONTAL';set.itemSpacing=16;set.paddingTop=16;set.paddingBottom=16;set.paddingLeft=16;set.paddingRight=16;
 async function build(d,theme) {
   if(d.kind==='text') {const n=await label(d.text,d.size,theme,d.role);n.resize(d.w,d.h);if(d.bold)n.fontName=boldFont;return n;}
   if(d.kind==='button') {const n=buttons[theme+'/'+d.style].createInstance();n.name=d.text;n.resize(d.w,d.h);const tx=n.findOne(x=>x.type==='TEXT');tx.characters=d.text;return n;}
   const n=figma.createFrame();n.name=d.name||d.kind;n.resize(d.w,d.h);n.fills=d.fill?[paint(theme,d.fill)]:[];n.clipsContent=false;
   if(d.kind==='checkbox'){n.resize(18,18);n.cornerRadius=4;n.fills=[paint(theme,d.checked?'Accent':'Surface')];n.strokes=[paint(theme,'Muted')];if(d.checked){const v=figma.createVector();v.vectorPaths=[{windingRule:'NONE',data:'M 3 9 L 7 13 L 15 4'}];v.fills=[];v.strokes=[paint(theme,'Window')];v.strokeWeight=2;n.appendChild(v);}return n;}
   if(d.border){n.strokes=[paint(theme,'Border')];n.strokeWeight=1;n.cornerRadius=12;}
   if(d.kind==='bar'){n.fills=[paint(theme,'Border')];const progress=figma.createRectangle();progress.resize(d.w*.42,3);progress.fills=[paint(theme,'Accent')];n.appendChild(progress);}
   if(d.children){n.layoutMode=d.kind==='row'?'HORIZONTAL':'VERTICAL';n.primaryAxisAlignItems=d.align||'MIN';n.primaryAxisSizingMode='FIXED';n.counterAxisSizingMode='FIXED';n.itemSpacing=d.gap;n.paddingTop=d.padY===undefined?d.pad:d.padY;n.paddingBottom=d.padY===undefined?d.pad:d.padY;n.paddingLeft=d.pad;n.paddingRight=d.pad;
     for(const child of d.children){const c=await build(child,theme);n.appendChild(c);c.layoutSizingHorizontal='FIXED';c.layoutSizingVertical='FIXED';}}
   return n;
 }
 const frames=[];
 for(let index=0;index<DATA.screens.length;index++){const s=DATA.screens[index];const n=await build(s.root,s.theme);n.name=s.theme+' / '+s.name;page.appendChild(n);n.x=(index%2)*1380;n.y=Math.floor(index/2)*920;n.setExplicitVariableModeForCollection(collection,s.theme==='Dark'?darkMode:collection.defaultModeId);frames.push(n);}
 figma.viewport.scrollAndZoomIntoView([frames[0]]);
 figma.closePlugin('已在新页面创建可编辑改版稿；旧稿保留。字体：'+font.family);
}
main().catch(error=>figma.closePlugin('创建失败：'+error.message));
'''
plugin=OUT/'figma-plugin';plugin.mkdir(exist_ok=True)
(plugin/'manifest.json').write_text(json.dumps(dict(name='FlowPack 资源转移改版（本地设计源）',api='1.0.0',main='code.js',editorType=['figma'],documentAccess='dynamic-page',networkAccess=dict(allowedDomains=['none'])),ensure_ascii=False,indent=2),encoding='utf-8')
(plugin/'code.js').write_text('const DATA='+json.dumps(payload,ensure_ascii=False,separators=(',',':'))+';\n'+PLUGIN,encoding='utf-8')
(OUT/'README.md').write_text('''# FlowPack 资源转移改版设计源

本地 SVG 的文字与矢量可编辑；`design-source.json` 保存语义层级。`figma-plugin/` 是用于在现有 Figma 文件创建新页面的原生开发插件源文件，使用文本、自动布局、颜色变量与复用按钮组件，保留旧稿。

**云端状态：未同步。** 本轮 Figma MCP 返回 Starter 调用额度耗尽，直接编辑也失败。插件仅通过 JavaScript 语法检查，尚未在 Figma 运行，不能替代云端图层验收。按钮可编辑，但原生字体可能随 Figma 可用字体变化。画面里的资源、节点名称是设计示例，版本未确认不填虚构版本号。

## 使用插件

在 https://www.figma.com/design/ATifwFUoSDhcXPgeCRVgfS 打开现有文件，从 Figma 的开发插件菜单导入本目录 `figma-plugin/manifest.json`，运行后会新增“FlowPack · 资源转移改版 · 2026-10-05”页面。需要可用的 Microsoft YaHei UI、Microsoft YaHei 或 Noto Sans SC 字体。再次运行会再新增一页，旧稿不会被覆盖。

包含浅色、深色首页、工作流/模型/节点目录树、仅节点独立导出、统一导入、日志和关于作者，以及加载、离线、空内容、错误状态。WPF 的真实截图在 `artifacts/acceptance/resource-transfer-20261005/ui-final`，只作实现验证，未放入设计图层。

通过 `python build/Generate-TransferDesign.py` 可重新生成本地设计源。SVG 负责可编辑视觉参考；WPF 实现与 DESIGN.md 是产品行为和共享令牌的依据，源码映射见 `docs/RESOURCE_TRANSFER_2026-10-05.md`。
''',encoding='utf-8')
print(f'Editable SVGs: {len(SCREENS)}; native plugin: {plugin}; cloud sync: pending (quota)')
