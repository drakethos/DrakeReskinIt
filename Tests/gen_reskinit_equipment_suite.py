# Writes Tests/suites/DrakesReskinIt-Equipment: ReskinIt with equipment / slot mods (ExtraSlots, AzuEPI, InventorySlots, SeneaL UI).
# Every slot type gets an item that is reskinned (icon, icon colour, model, model colour); the tests then read what the slot UI
# draws and what the character wears. Run the suite once per equipment mod (see DrakesRenameIt/docs/COMPATIBILITY.md).
# Separate from gen_reskinit_suite.py so running either one never touches the other's folder.
import json, os, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'suites', 'DrakesReskinIt', 'suite.json')
OUT = os.path.join(HERE, 'suites', 'DrakesReskinIt-Equipment')

# (item prefab, slot label, look prefab used as the new icon, icon colour, model look or None, model colour or None, model slot for wornModel)
ITEMS = [
    ('HelmetTrollLeather', 'Helmet', 'HelmetBronze', '#FF0000', 'HelmetBronze', '#FF8800', 'helmet'),
    ('ArmorTrollLeatherChest', 'Chest', 'ArmorBronzeChest', '#00FF00', 'ArmorBronzeChest', None, 'chest'),
    ('ArmorTrollLeatherLegs', 'Legs', 'ArmorBronzeLegs', '#0000FF', 'ArmorBronzeLegs', None, 'legs'),
    ('CapeDeerHide', 'Cape', 'CapeTrollHide', '#FFFF00', 'CapeTrollHide', None, 'shoulder'),
    ('Wishbone', 'Utility', 'BeltStrength', '#FF00FF', None, None, None),
    ('ShieldWood', 'Shield', 'ShieldBronzeBuckler', '#FF8000', 'ShieldBronzeBuckler', None, 'left'),
    ('AxeStone', 'Weapon', 'AxeBronze', '#00FFFF', 'AxeBronze', '#00FF00', 'right'),
    ('CookedMeat', 'Food', 'CookedDeerMeat', '#88FF00', None, None, None),
    ('ArrowWood', 'Ammo', 'ArrowFlint', '#8800FF', None, None, None),
]
EQUIPPED = ('Helmet', 'Chest', 'Legs', 'Cape', 'Utility', 'Shield', 'Weapon')

NOERR = {'check': 'noErrors', 'source': 'DrakesReskinIt'}

def hook(name, args=None, **kw):
    s = {'do': 'hook', 'name': 'reskin.' + name}
    if args:
        s['args'] = args
    s.update(kw)
    return s

def wait(sec):
    return {'do': 'wait', 'seconds': sec}

def shot(name):
    return {'do': 'screenshot', 'name': name}

def setup():
    steps = []
    for prefab, slot, *_ in ITEMS:
        steps.append({'do': 'give', 'prefab': prefab, 'crafter': 'me', **({'equip': True} if slot in EQUIPPED else {})})
    return steps

tests = {}

tests['RK-01'] = ('icons: every slot type shows the new icon in the slot UI and in ItemData.GetIcon', [
    *setup(),
    *[hook('apply', {'prefab': p, 'icon': look, 'iconTint': tint}, expect='', label=f'{slot}: apply the {look} icon, colour {tint}')
      for p, slot, look, tint, *_ in ITEMS],
    hook('open') if False else {'do': 'hook', 'name': 'renameit.openInventory', 'expect': True, 'label': 'open the inventory (RenameIt test hook)'},
    wait(1.5),
    shot('reskinned-slots'),
    *[hook('itemIconIs', {'prefab': p, 'look': look}, expect=True, label=f'{slot}: GetIcon is the {look} icon (what other windows use)')
      for p, slot, look, *_ in ITEMS],
    *[hook('slotIcons', {'look': look}, contains='n=', notContains='n=0', label=f'{slot}: a slot in the open inventory draws the {look} icon')
      for p, slot, look, *_ in ITEMS],
    *[hook('slotIcons', {'look': look}, contains=tint, label=f'{slot}: that slot is drawn in {tint}')
      for p, slot, look, tint, *_ in ITEMS],
])

tests['RK-02'] = ('models: what the character wears changes, with its colour', [
    *setup(),
    *[hook('apply', {'prefab': p, 'model': model, **({'modelTint': mtint} if mtint else {})}, expect='', label=f'{slot}: wear the {model} model')
      for p, slot, look, tint, model, mtint, wslot in ITEMS if model],
    wait(1.0),
    *[hook('wornModel', {'slot': wslot}, expect=model, label=f'{slot}: the character wears {model}')
      for p, slot, look, tint, model, mtint, wslot in ITEMS if model],
    *[hook('wornTint', {'slot': wslot}, expect=mtint, label=f'{slot}: the worn model is drawn in {mtint}')
      for p, slot, look, tint, model, mtint, wslot in ITEMS if model and mtint and wslot in ('right', 'left', 'helmet')],
    shot('worn-models'),
])

tests['RK-03'] = ('previewer: the Reskin window shows the new icon, model and colours before Apply', [
    {'do': 'give', 'prefab': 'CapeDeerHide', 'crafter': 'me', 'equip': True},
    {'do': 'itemClick', 'prefab': 'CapeDeerHide', 'index': 0, 'mods': 'Shift'},
    wait(0.6),
    {'do': 'click', 'button': 'Reskin'},
    wait(0.6),
    hook('windowOpen', expect=True, label='the Reskin window is open', stop=True),
    hook('pick', {'look': 'CapeTrollHide'}, expect=True, label='pick the troll cape look'),
    hook('pickColor', {'target': 'icon', 'hex': '#ff0000'}, expect=True, label='icon colour red'),
    hook('pickColor', {'target': 'model', 'hex': '#00ff00'}, expect=True, label='model colour green'),
    wait(0.4),
    hook('previewShows', contains='iconNow=', label='read the preview'),
    hook('previewShows', regex=r'.*iconNow=[^;]*TrollHide[^;]*/#FF0000.*', label='the icon preview shows the troll cape in red'),
    hook('previewShows', regex=r'.*modelNow=[^;]*TrollHide[^;]*/#00FF00.*', label='the model preview shows the troll cape in green'),
    hook('previewShows', regex=r'.*iconWas=(?!none).*', label='and still shows the original beside it'),
    shot('previewer'),
    {'do': 'click', 'button': 'Apply'},
    wait(0.6),
    hook('slotIcons', {'look': 'CapeTrollHide'}, contains='#FF0000', label='after Apply a slot in the inventory draws it red'),
    hook('wornModel', {'slot': 'shoulder'}, expect='CapeTrollHide', label='the character wears it'),
    shot('after-apply'),
    {'do': 'click', 'button': 'Close'},
    wait(0.3),
    {'do': 'closeUi'},
])

tests['RK-04'] = ('reset: every slot goes back to its own icon and colour', [
    *setup(),
    *[hook('apply', {'prefab': p, 'icon': look, 'iconTint': tint}, expect='', label=f'{slot}: reskin') for p, slot, look, tint, *_ in ITEMS],
    *[hook('reset', {'prefab': p}, expect=True, label=f'{slot}: reset') for p, slot, *_ in ITEMS],
    {'do': 'hook', 'name': 'renameit.openInventory', 'expect': True, 'label': 'open the inventory (RenameIt test hook)'},
    wait(1.5),
    *[hook('itemIconIs', {'prefab': p, 'look': look}, expect=False, label=f'{slot}: GetIcon is the original again') for p, slot, look, *_ in ITEMS],
    *[hook('slotIcons', {'look': look}, contains='n=0', label=f'{slot}: no slot still draws the look icon') for p, slot, look, *_ in ITEMS],
    shot('reset-slots'),
])

if os.path.isdir(OUT):
    shutil.rmtree(OUT)
os.makedirs(OUT)
with open(SRC, encoding='utf-8') as f:
    suite = json.load(f)
suite['name'] = 'DrakesReskinIt-Equipment'
suite['description'] = ('ReskinIt with equipment / slot mods: icon, icon colour, equipped model and the Reskin window preview, for every slot type. '
                       'Run once per equipment mod. Needs DrakesRenameIt\'s test hooks (renameit.openInventory) to open the inventory.')
suite.setdefault('requireMods', []).append({'guid': 'com.drakemods.renameit', 'minVersion': '1.4.0'})
with open(os.path.join(OUT, 'suite.json'), 'w', encoding='utf-8') as f:
    json.dump(suite, f, indent=2, ensure_ascii=False)
for tid, (title, steps) in tests.items():
    t = {'name': f'{tid} {title}', 'description': 'Equipment-mod compatibility for ReskinIt.', 'steps': steps, 'asserts': [NOERR], 'timeout': 180}
    with open(os.path.join(OUT, f'{tid}.json'), 'w', encoding='utf-8') as f:
        json.dump(t, f, indent=2, ensure_ascii=False)
print(len(tests), 'tests written')
