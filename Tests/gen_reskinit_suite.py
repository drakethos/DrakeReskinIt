"""Writes the DrakesTestLab suite for ReskinIt into Tests/suites/DrakesReskinIt (run: python gen_reskinit_suite.py).
The build copies it into the test profiles; Tests/ is never packaged."""
import json, os, shutil

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'suites', 'DrakesReskinIt')
GUID = 'com.drakesworkshop.reskinit'
NOERR = {'check': 'noErrors', 'source': 'DrakesReskinIt'}


def H(name, expect=None, label=None, contains=None, notContains=None, stop=False, **args):
    s = {'do': 'hook', 'name': 'reskin.' + name}
    if args:
        s['args'] = args
    for k, v in (('expect', expect), ('contains', contains), ('notContains', notContains), ('label', label)):
        if v is not None:
            s[k] = v
    if stop:
        s['stop'] = True
    return s


def C(values):
    return {'do': 'setConfig', 'values': values}


def craft(prefab, by=None):
    s = {'do': 'craft', 'prefab': prefab}
    if by:
        s['by'] = by
    return s


def play(who):
    return {'do': 'playAs', 'who': who}


def check(kind, label=None, **f):
    s = {'do': 'check', 'check': kind, **f}
    if label:
        s['label'] = label
    return s


VIP = {'02 - Admin/AllowAdminOverride': 'true', '02 - Admin/VipOnlyOverride': 'true', '02 - Admin/VipList': 'Nacho-VIP'}
tests = {}


def T(tid, title, steps, desc=''):
    tests[tid] = {'name': f'{tid} {title}', 'description': desc, 'steps': steps, 'asserts': [NOERR]}


T('R-01', 'change the icon', [
    craft('AxeStone'),
    H('can', True, 'icons may be changed', prefab='AxeStone', kind='icon'),
    H('apply', '', 'apply the flint axe icon', stop=True, prefab='AxeStone', icon='AxeFlint'),
    H('icon', label='icon override stored', contains='AxeFlint', prefab='AxeStone'),
    {'do': 'screenshot', 'name': 'icon'},
])

T('R-02', 'change the equipped model (same kind only)', [
    craft('AxeStone'),
    H('modelFits', True, 'another one-handed weapon fits', prefab='AxeStone', look='AxeFlint'),
    H('modelFits', False, 'a chest armor does not', prefab='AxeStone', look='ArmorLeatherChest'),
    H('apply', "That model doesn't fit this item", 'refused with a message', prefab='AxeStone', model='ArmorLeatherChest'),
    H('model', '', 'nothing changed', prefab='AxeStone'),
    H('apply', '', 'apply the flint axe model', stop=True, prefab='AxeStone', model='AxeFlint'),
    H('model', contains='AxeFlint', prefab='AxeStone'),
    {'do': 'give', 'prefab': 'AxeStone', 'equip': True},
    {'do': 'screenshot', 'name': 'model'},
])

T('R-03', 'colors on icon and model', [
    craft('AxeStone'),
    H('apply', '', prefab='AxeStone', iconTint='#ff0000', modelTint='#00ff00'),
    H('iconTint', '#FF0000', prefab='AxeStone'),
    H('modelTint', '#00FF00', prefab='AxeStone'),
])

T('R-04', 'reset restores the original look (free)', [
    C({'05 - Cost/CostEnabled': 'true', '05 - Cost/CostItem': 'Coins', '05 - Cost/CostAmount': '10'}),
    craft('AxeStone'),
    {'do': 'give', 'prefab': 'Coins', 'count': 10},
    H('apply', '', prefab='AxeStone', icon='AxeFlint', iconTint='#ff0000'),
    H('reset', True, prefab='AxeStone'),
    H('icon', '', 'icon back to the original', prefab='AxeStone'),
    H('iconTint', '', 'color gone', prefab='AxeStone'),
    check('noItem', 'the reskin cost was paid once, the reset was free (no refund, no charge)', prefab='Coins'),
])

T('R-05', 'feature switches', [
    craft('AxeStone'),
    C({'03 - Features/EnableIcon': 'false'}),
    H('can', False, 'EnableIcon off', prefab='AxeStone', kind='icon'),
    H('apply', "Icons can't be changed on this item", prefab='AxeStone', icon='AxeFlint'),
    H('can', True, 'model still allowed', prefab='AxeStone', kind='model'),
    C({'03 - Features/EnableModel': 'false'}),
    H('can', False, 'EnableModel off', prefab='AxeStone', kind='model'),
    C({'03 - Features/EnableColor': 'false'}),
    H('can', False, 'EnableColor off', prefab='AxeStone', kind='color'),
    H('apply', 'Colors are turned off for this item', prefab='AxeStone', iconTint='#ff0000'),
    H('tabAvailable', False, 'all three off hides the Reskin tab', prefab='AxeStone'),
])

T('R-06', 'Access = AdminsAndVips', [
    C({'03 - Features/Access': 'AdminsAndVips'}),
    craft('AxeStone'),
    H('can', False, 'a normal player may not', prefab='AxeStone', kind='icon'),
    C(VIP),
    play('Nacho-VIP'),
    H('elevated', True, 'Nacho-VIP is elevated'),
    H('can', True, 'a VIP may', prefab='AxeStone', kind='icon'),
    H('apply', '', prefab='AxeStone', icon='AxeFlint'),
    play('me'),
])

T('R-07', 'exclusions', [
    C({'04 - Exclusions/ExcludedNames': 'AxeStone'}),
    craft('AxeStone'),
    craft('AxeFlint'),
    H('can', False, 'excluded by name', prefab='AxeStone', kind='icon'),
    H('can', True, 'other items fine', prefab='AxeFlint', kind='icon'),
    C({'04 - Exclusions/Allowlist': 'AxeStone'}),
    H('can', label='observed: allowlist vs excluded name (informational)', prefab='AxeStone', kind='icon'),
])

T('R-08', 'RequireDiscovery = Seen', [
    C({'03 - Features/RequireDiscovery': 'Seen'}),
    craft('AxeStone'),
    H('lockReason', label='an item you never held is locked', contains='Not discovered', look='SwordSilver'),
    H('apply', 'Not discovered yet', 'and cannot be applied', prefab='AxeStone', icon='SwordSilver'),
    {'do': 'give', 'prefab': 'SwordSilver'},
    {'do': 'wait', 'seconds': 0.3},
    H('lockReason', '', 'holding it once unlocks the look', look='SwordSilver'),
    H('apply', '', prefab='AxeStone', icon='SwordSilver'),
])

T('R-09', 'RequireDiscovery = Crafted', [
    C({'03 - Features/RequireDiscovery': 'Crafted'}),
    H('forgetCrafted', label='forget earlier bronze axe crafts (the list lives in the character file)', prefab='AxeBronze'),
    craft('AxeStone'),
    {'do': 'give', 'prefab': 'AxeBronze'},
    H('lockReason', 'Craft one first', 'holding a craftable item is not enough', look='AxeBronze'),
    craft('AxeBronze'),
    H('lockReason', '', 'crafting it unlocks the look', look='AxeBronze'),
    H('lockReason', label='non-craftable items only need to be seen', contains='Not discovered', look='TrophyDeer'),
])

T('R-10', 'progression gate (world keys)', [
    C({'03 - Features/ProgressionGate': 'true'}),
    craft('AxeStone'),
    {'do': 'give', 'prefab': 'SwordIron'},
    H('lockReason', label='iron gear waits for The Elder', contains='Defeat', look='SwordIron'),
    C({'03 - Features/ProgressionOverrides': 'SwordIron=none'}),
    H('lockReason', '', 'ProgressionOverrides =none unlocks it', look='SwordIron'),
    C({'03 - Features/ProgressionOverrides': '', '03 - Features/KeyAliases': 'defeated_gdking=mod_iron_test'}),
    H('lockReason', label='KeyAliases: now waits for the custom key', contains='mod_iron_test', look='SwordIron'),
    {'do': 'console', 'command': 'setkey mod_iron_test'},
    {'do': 'wait', 'seconds': 0.3},
    H('lockReason', '', 'setting that key unlocks it', look='SwordIron'),
    {'do': 'console', 'command': 'removekey mod_iron_test'},
])

T('R-11', 'apply cost', [
    C({'05 - Cost/CostEnabled': 'true', '05 - Cost/CostItem': 'Coins', '05 - Cost/CostAmount': '10'}),
    craft('AxeStone'),
    {'do': 'give', 'prefab': 'Coins', 'count': 5},
    H('costLine', label='the window shows the cost', contains='10x'),
    H('apply', label='not enough coins', contains='You need 10x', prefab='AxeStone', icon='AxeFlint'),
    H('icon', '', 'nothing applied', prefab='AxeStone'),
    {'do': 'give', 'prefab': 'Coins', 'count': 5},
    H('apply', '', 'paid and applied', prefab='AxeStone', icon='AxeFlint'),
    check('noItem', '10 coins spent', prefab='Coins'),
    C({'05 - Cost/CostItem': 'NotAnItem'}),
    H('apply', '', 'an invalid cost item makes reskins free (warns in the log)', prefab='AxeStone', icon='Club'),
    check('logContains', 'the bad CostItem is reported', text='is not an item prefab'),
])

T('R-12', 'VIPs skip discovery and progression', [
    C({**VIP, '03 - Features/RequireDiscovery': 'Seen', '03 - Features/ProgressionGate': 'true'}),
    play('Nacho-VIP'),
    craft('AxeStone'),
    H('lockReason', '', 'never seen, but a VIP may use it', look='SwordSilver'),
    H('lockReason', '', 'past the progression gate too', look='SwordIron'),
    play('me'),
])

# ---------------------------------------------------------------- the real window, grid and what is drawn
# The window is opened the player's way: Shift + right-click on the slot, then the Reskin tab.

def wait(s=0.3):
    return {'do': 'wait', 'seconds': s}


def open_window(prefab='AxeStone', index=0):
    return [{'do': 'itemClick', 'prefab': prefab, 'index': index, 'mods': 'Shift'}, wait(0.4),
            {'do': 'click', 'button': 'Reskin'}, wait(0.4),
            H('windowOpen', True, 'the Reskin window is open', stop=True)]


def close_window():
    return [{'do': 'click', 'button': 'Close'}, wait(0.2), {'do': 'closeUi'}]


T('R-13', 'Shift + right-click and the Reskin tab open the window', [
    craft('AxeStone'),
    *open_window(),
    H('grid', label='opens on looks of the same type', contains='filter=Same type'),
    check('uiVisible', 'Apply button showing', name='Apply'),
    {'do': 'screenshot', 'name': 'reskin-window'},
    *close_window(),
    H('windowOpen', False, 'Close closes it'),
])

T('R-14', 'pick an icon in the grid, Apply: the inventory shows it', [
    craft('AxeStone'),
    *open_window(),
    H('pick', True, 'click the flint axe in the grid', look='AxeFlint'),
    H('pending', label='picked, not applied yet', contains='icon=AxeFlint'),
    H('icon', '', 'nothing changed before Apply', prefab='AxeStone'),
    {'do': 'click', 'button': 'Apply'}, wait(),
    H('icon', label='applied', contains='AxeFlint', prefab='AxeStone'),
    H('iconIs', True, 'the inventory slot draws the flint axe icon', prefab='AxeStone', look='AxeFlint'),
    H('itemIcon', label='ItemData.GetIcon (what other windows use) too', notContains='Stone', prefab='AxeStone'),
    {'do': 'screenshot', 'name': 'icon-in-inventory'},
    *close_window(),
])

T('R-15', 'model mode: only models that fit, and the character wears it', [
    craft('AxeStone'),
    H('equip', True, prefab='AxeStone'),
    H('wornModel', 'AxeStone', 'wearing the stone axe', slot='right'),
    *open_window(),
    H('setMode', True, mode='Model'),
    H('cell', 'absent', 'chest armor is not offered as a model for an axe', look='ArmorLeatherChest'),
    H('cell', 'unlocked', 'another axe is', look='AxeFlint'),
    H('pick', True, look='AxeFlint'),
    {'do': 'click', 'button': 'Apply'}, wait(0.5),
    H('wornModel', 'AxeFlint', 'the character now holds the flint axe model', slot='right'),
    {'do': 'screenshot', 'name': 'model-swap'},
    *close_window(),
])

T('R-16', 'colour picker: icon and model colours are drawn', [
    craft('AxeStone'),
    H('equip', True, prefab='AxeStone'),
    *open_window(),
    H('pickColor', True, 'icon colour red', target='icon', hex='#ff0000'),
    H('pickColor', True, 'model colour green', target='model', hex='#00ff00'),
    H('pending', label='both colours picked', contains='iconTint=#FF0000'),
    {'do': 'click', 'button': 'Apply'}, wait(0.5),
    H('iconShown', label='the inventory icon is drawn red', contains='color=#FF0000', prefab='AxeStone'),
    H('wornTint', '#00FF00', 'the held axe is drawn green', slot='right'),
    {'do': 'screenshot', 'name': 'colours'},
    *close_window(),
])

T('R-17', 'Reset in the window (with its confirm) restores everything', [
    craft('AxeStone'),
    H('equip', True, prefab='AxeStone'),
    H('apply', '', prefab='AxeStone', icon='AxeFlint', model='AxeFlint', iconTint='#ff0000', modelTint='#00ff00'),
    *open_window(),
    {'do': 'click', 'button': 'Reset'}, wait(0.3),
    {'do': 'click', 'button': 'Reset'}, wait(0.5),
    H('icon', '', 'icon back to the original', prefab='AxeStone'),
    H('iconIs', True, 'the slot draws the stone axe again', prefab='AxeStone', look='AxeStone'),
    H('wornModel', 'AxeStone', 'the original model is back', slot='right'),
    H('wornTint', '', 'no colour on it', slot='right'),
    *close_window(),
])

T('R-18', 'presets: save from one item, load on another', [
    H('deletePresetFile', True),
    craft('AxeStone'),
    craft('AxeStone'),
    *open_window(index=0),
    H('pick', True, look='AxeFlint'),
    H('pickColor', True, target='icon', hex='#3366ff'),
    {'do': 'click', 'button': 'Save'}, wait(0.3),
    {'do': 'type', 'field': '*', 'text': 'Test Preset'},
    {'do': 'click', 'button': 'Save'}, wait(0.3),
    H('presets', label='saved', contains='Test Preset'),
    *close_window(),
    *open_window(index=1),
    H('loadPreset', True, preset='Test Preset'),
    H('pending', label='the preset fills the picks', contains='icon=AxeFlint'),
    {'do': 'click', 'button': 'Apply'}, wait(),
    H('icon', label='second axe has the preset icon', contains='AxeFlint', prefab='AxeStone', index=1),
    H('iconTint', '#3366FF', 'and its colour', prefab='AxeStone', index=1),
    *close_window(),
    H('deletePresetFile', True),
])

T('R-19', 'ShowUndiscovered: locked looks dark, or hidden', [
    C({'03 - Features/RequireDiscovery': 'Seen', '03 - Features/ShowUndiscovered': 'true'}),
    craft('AxeStone'),
    *open_window(),
    H('setFilter', True, filter='All'),
    H('cell', label='an undiscovered look is shown, dark and locked', contains='locked', look='SwordSilver'),
    H('grid', label='the count says how many are unlocked', notContains='unlocked=0 '),
    H('pick', True, 'clicking it...', look='SwordSilver'),
    H('pending', label='...picks nothing (it only shows the reason)', notContains='SwordSilver'),
    {'do': 'screenshot', 'name': 'locked-dark'},
    *close_window(),
    C({'03 - Features/ShowUndiscovered': 'false'}),
    *open_window(),
    H('setFilter', True, filter='All'),
    H('cell', 'absent', 'with ShowUndiscovered off it is hidden', look='SwordSilver'),
    H('cell', 'unlocked', 'discovered looks still show', look='AxeStone'),
    *close_window(),
])

T('R-20', 'SameTypeOnly', [
    craft('AxeStone'),
    *open_window(),
    H('setMode', True, 'Icon mode (Model and Both only offer models that fit the slot)', mode='Icon'),
    H('setFilter', True, filter='All'),
    H('gridCategories', label='All shows every category', contains=','),
    *close_window(),
    C({'03 - Features/SameTypeOnly': 'true'}),
    *open_window(),
    H('setMode', True, mode='Icon'),
    H('setFilter', label='the All filter is hidden (informational)', filter='All'),
    H('gridCategories', label='SameTypeOnly keeps it to one category even on All', notContains=','),
    H('cell', 'absent', 'a trophy is not offered for an axe', look='TrophyDeer'),
    *close_window(),
])

T('R-21', 'known / unknown: holding an item unlocks its look in the grid', [
    C({'03 - Features/RequireDiscovery': 'Seen'}),
    craft('AxeStone'),
    *open_window(),
    H('setFilter', True, filter='All'),
    H('cell', label='unknown', contains='Not discovered', look='SwordSilver'),
    *close_window(),
    {'do': 'give', 'prefab': 'SwordSilver'}, wait(0.3),
    *open_window(),
    H('setFilter', True, filter='All'),
    H('cell', 'unlocked', 'known now', look='SwordSilver'),
    H('pick', True, look='SwordSilver'),
    {'do': 'click', 'button': 'Apply'}, wait(),
    H('icon', label='applied', contains='SwordSilver', prefab='AxeStone'),
    *close_window(),
])

T('R-22', 'RequireDiscovery Crafted and the progression gate in the grid', [
    C({'03 - Features/RequireDiscovery': 'Crafted'}),
    H('forgetCrafted', label='forget earlier bronze axe crafts (the list lives in the character file)', prefab='AxeBronze'),
    craft('AxeStone'),
    {'do': 'give', 'prefab': 'AxeBronze'},
    *open_window(),
    H('setFilter', True, filter='All'),
    H('cell', label='held but never crafted', contains='Craft one first', look='AxeBronze'),
    *close_window(),
    C({'03 - Features/ProgressionGate': 'true'}),
    *open_window(),
    H('setFilter', True, filter='All'),
    H('cell', label='the progression gate is named first when both apply', contains='Defeat Eikthyr', look='AxeBronze'),
    H('cell', label='iron waits for The Elder', contains='Defeat', look='SwordIron'),
    *close_window(),
    C({'03 - Features/ProgressionGate': 'false'}),
    craft('AxeBronze'),
    *open_window(),
    H('setFilter', True, filter='All'),
    H('cell', 'unlocked', 'crafted once: unlocked', look='AxeBronze'),
    *close_window(),
])

T('R-23', 'exclusions by category and the allowlist', [
    C({'04 - Exclusions/ExcludedCategory': 'Axes'}),
    craft('AxeStone'),
    craft('AxeFlint'),
    craft('Club'),
    H('can', False, 'axes excluded by category (skill Axes)', prefab='AxeStone', kind='icon'),
    H('tabAvailable', False, 'no Reskin tab for an excluded item', prefab='AxeStone'),
    H('can', True, 'a club is not an axe', prefab='Club', kind='icon'),
    C({'04 - Exclusions/Allowlist': 'AxeStone'}),
    H('can', True, 'the allowlist wins over the category', prefab='AxeStone', kind='icon'),
    H('can', False, 'other axes still excluded', prefab='AxeFlint', kind='icon'),
    C({'04 - Exclusions/ExcludedCategory': 'OneHandedWeapon'}),
    H('can', False, 'item types work as categories too', prefab='Club', kind='icon'),
])

T('R-24', 'cost through the window', [
    C({'05 - Cost/CostEnabled': 'true', '05 - Cost/CostItem': 'Coins', '05 - Cost/CostAmount': '10'}),
    craft('AxeStone'),
    {'do': 'give', 'prefab': 'Coins', 'count': 5},
    *open_window(),
    H('pick', True, look='AxeFlint'),
    {'do': 'click', 'button': 'Apply'}, wait(),
    H('icon', '', 'not enough coins: nothing applied', prefab='AxeStone'),
    check('hasItem', 'coins kept', prefab='Coins', count=5),
    {'do': 'give', 'prefab': 'Coins', 'count': 5},
    {'do': 'click', 'button': 'Apply'}, wait(),
    H('icon', label='paid and applied', contains='AxeFlint', prefab='AxeStone'),
    check('noItem', '10 coins taken', prefab='Coins'),
    *close_window(),
])

T('R-25', 'armour models: helmet and chest swap on the character', [
    craft('HelmetLeather'),
    craft('ArmorLeatherChest'),
    H('equip', True, prefab='HelmetLeather'),
    H('equip', True, prefab='ArmorLeatherChest'),
    H('wornModel', 'HelmetLeather', slot='helmet'),
    H('apply', '', 'leather helmet looks like a bronze one', prefab='HelmetLeather', model='HelmetBronze', modelTint='#ff8800'),
    H('apply', '', 'leather chest looks like bronze', prefab='ArmorLeatherChest', model='ArmorBronzeChest'),
    wait(0.5),
    H('wornModel', 'HelmetBronze', 'the helmet model changed', slot='helmet'),
    H('wornModel', 'ArmorBronzeChest', 'the chest model changed', slot='chest'),
    H('wornTint', '#FF8800', 'the helmet colour is drawn', slot='helmet'),
    H('modelFits', False, 'a helmet does not fit the chest slot', prefab='ArmorLeatherChest', look='HelmetBronze'),
    {'do': 'screenshot', 'name': 'armour-swap'},
])

T('R-26', 'feature switches in the window', [
    craft('AxeStone'),
    C({'03 - Features/EnableModel': 'false'}),
    *open_window(),
    H('grid', label='only icons when models are off', contains='mode=Icon'),
    *close_window(),
    C({'03 - Features/EnableModel': 'true', '03 - Features/EnableIcon': 'false'}),
    *open_window(),
    H('grid', label='only models when icons are off', contains='mode=Model'),
    *close_window(),
])

suite = {
    'name': 'DrakesReskinIt',
    'description': "Automated regression for Drake's ReskinIt: icons, models, colors, reset, feature switches, access, exclusions, discovery rules, "
                   'progression keys, cost and VIP bypass. Applies run through ReskinApply (the Reskin window\'s own rules).',
    'requireMods': [{'guid': GUID, 'minVersion': '0.1.0'}],
    'configOverrides': [{'guid': GUID, 'section': k.split('/')[0], 'key': k.split('/')[1], 'value': v} for k, v in {
        # own admin settings with override off: the single-player host is an admin and would bypass everything
        '02 - Admin/AdminSource': 'Own', '02 - Admin/AllowAdminOverride': 'false', '02 - Admin/VipOnlyOverride': 'false', '02 - Admin/VipList': '',
        '04 - Exclusions/ExclusionSource': 'Own', '04 - Exclusions/ExcludedNames': '', '04 - Exclusions/ExcludedCategory': '', '04 - Exclusions/Allowlist': '',
        '03 - Features/EnableIcon': 'true', '03 - Features/EnableModel': 'true', '03 - Features/EnableColor': 'true',
        '03 - Features/Access': 'Everyone', '03 - Features/SameTypeOnly': 'false', '03 - Features/RequireDiscovery': 'Off',
        '03 - Features/ShowUndiscovered': 'true', '03 - Features/ProgressionGate': 'false',
        '03 - Features/KeyAliases': '', '03 - Features/ProgressionOverrides': '',
        '05 - Cost/CostEnabled': 'false', '05 - Cost/CostItem': 'Coins', '05 - Cost/CostAmount': '10',
    }.items()],
    'setup': {'timeOfDay': 0.5, 'freezeTime': 1, 'god': 1, 'noBuild': 'off', 'hideUnlockPopups': 1},
    'resetBetweenTests': True,
}

if os.path.isdir(OUT):
    shutil.rmtree(OUT)
os.makedirs(OUT)
with open(os.path.join(OUT, 'suite.json'), 'w', encoding='utf-8') as f:
    json.dump(suite, f, indent=2, ensure_ascii=False)
for tid, t in tests.items():
    with open(os.path.join(OUT, f'{tid}.json'), 'w', encoding='utf-8') as f:
        json.dump(t, f, indent=2, ensure_ascii=False)
print(len(tests), 'ReskinIt tests')

RI = 'com.drakesworkshop.renameit'
LINKED_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'suites', 'DrakesReskinIt-Linked')
ltests = {}


def L(tid, title, steps, desc=''):
    ltests[tid] = {'name': f'{tid} {title}', 'description': desc, 'steps': steps,
                   'asserts': [NOERR, {'check': 'noErrors', 'source': 'DrakesRenameit'}]}


def RC(values):
    return {'do': 'setConfig', 'guid': RI, 'values': values}


def RH(name, expect=None, label=None, **args):
    s = {'do': 'hook', 'name': 'renameit.' + name, 'args': args}
    if expect is not None:
        s['expect'] = expect
    if label:
        s['label'] = label
    return s


RI_BASE = {'01 Admin/AllowAdminOverride': 'false', '01 Admin/VipOnlyOverride': 'false', '01 Admin/VipList': '',
           '03 Exclusions/ExcludedNames': '', '03 Exclusions/ExcludedCategory': '', '03 Exclusions/RenameAllowlist': ''}

L('RL-01', 'AdminSource = RenameIt: RenameIt\'s VIP list rules ReskinIt', [
    C({'02 - Admin/AdminSource': 'RenameIt', '03 - Features/Access': 'AdminsAndVips'}),
    RC({**RI_BASE, '01 Admin/AllowAdminOverride': 'true', '01 Admin/VipOnlyOverride': 'true', '01 Admin/VipList': 'Nacho-VIP'}),
    craft('AxeStone'),
    play('Nacho-VIP'),
    H('elevated', True, 'a RenameIt VIP is a ReskinIt VIP'),
    H('can', True, 'and may reskin with AdminsAndVips', prefab='AxeStone', kind='icon'),
    play('Bob'),
    H('can', False, 'Bob may not', prefab='AxeStone', kind='icon'),
    RC({'01 Admin/VipList': 'Nacho-VIP;Bob'}),
    H('elevated', True, 'adding Bob to RenameIt\'s list applies at once'),
    RC({'01 Admin/AllowAdminOverride': 'false'}),
    H('elevated', False, 'RenameIt override off: nobody elevated in ReskinIt either'),
    play('me'),
])

L('RL-02', 'AdminSource = Own ignores RenameIt\'s VIPs', [
    C({'02 - Admin/AdminSource': 'Own', '03 - Features/Access': 'AdminsAndVips'}),
    RC({**RI_BASE, '01 Admin/AllowAdminOverride': 'true', '01 Admin/VipOnlyOverride': 'true', '01 Admin/VipList': 'Nacho-VIP'}),
    play('Nacho-VIP'),
    H('elevated', False, 'a RenameIt VIP is not a ReskinIt VIP with Own'),
    C({'02 - Admin/AllowAdminOverride': 'true', '02 - Admin/VipOnlyOverride': 'true', '02 - Admin/VipList': 'Nacho-VIP'}),
    H('elevated', True, 'ReskinIt\'s own list works'),
    play('me'),
])

L('RL-03', 'ExclusionSource = RenameIt / Own / Merge', [
    craft('AxeStone'),
    craft('AxeFlint'),
    craft('Club'),
    RC({**RI_BASE, '03 Exclusions/ExcludedNames': 'AxeStone'}),
    C({'04 - Exclusions/ExclusionSource': 'RenameIt', '04 - Exclusions/ExcludedNames': 'AxeFlint'}),
    H('can', False, 'RenameIt: RenameIt\'s exclusion applies', prefab='AxeStone', kind='icon'),
    H('can', True, 'and ReskinIt\'s own list is ignored', prefab='AxeFlint', kind='icon'),
    C({'04 - Exclusions/ExclusionSource': 'Own'}),
    H('can', True, 'Own: RenameIt\'s exclusion ignored', prefab='AxeStone', kind='icon'),
    H('can', False, 'Own list applies', prefab='AxeFlint', kind='icon'),
    C({'04 - Exclusions/ExclusionSource': 'Merge'}),
    H('can', False, 'Merge: RenameIt\'s applies', prefab='AxeStone', kind='icon'),
    H('can', False, 'and ReskinIt\'s too', prefab='AxeFlint', kind='icon'),
    H('can', True, 'others fine', prefab='Club', kind='icon'),
    RC({'03 Exclusions/RenameAllowlist': 'AxeStone'}),
    C({'04 - Exclusions/ExclusionSource': 'RenameIt'}),
    H('can', label='observed: RenameIt\'s allowlist with ExclusionSource = RenameIt (informational)', prefab='AxeStone', kind='icon'),
])

L('RL-04', 'renames and reskins live side by side', [
    craft('AxeStone'),
    RH('edit', True, 'rename it', prefab='AxeStone', op='name', text='Shiny Axe'),
    H('apply', '', 'reskin it', prefab='AxeStone', icon='AxeFlint', iconTint='#ff0000'),
    RH('name', label='the name survives the reskin', prefab='AxeStone'),
    {'do': 'check', 'check': 'hook', 'name': 'renameit.name', 'args': {'prefab': 'AxeStone'}, 'contains': 'Shiny Axe', 'label': 'still Shiny Axe'},
    H('reset', True, 'Reskin reset', prefab='AxeStone'),
    {'do': 'check', 'check': 'hook', 'name': 'renameit.name', 'args': {'prefab': 'AxeStone'}, 'contains': 'Shiny Axe', 'label': 'Reskin reset keeps the name'},
    H('apply', '', prefab='AxeStone', icon='AxeFlint'),
    RH('reset', True, 'RenameIt Reset All', prefab='AxeStone', op='all'),
    H('icon', label='RenameIt reset keeps the reskin', contains='AxeFlint', prefab='AxeStone'),
])

L('RL-05', 'one window, both tabs; RenameIt\'s own key leaves Reskin on Shift', [
    craft('AxeStone'),
    {'do': 'itemClick', 'prefab': 'AxeStone', 'mods': 'Shift'}, wait(0.4),
    check('uiVisible', 'the Rename tab is there', name='Rename'),
    check('uiVisible', 'and the Reskin tab', name='Reskin'),
    RH('closeWindows'), {'do': 'closeUi'},
    RC({'11 UI-NotSynced/MenuOpenModifier': 'Ctrl'}),
    {'do': 'itemClick', 'prefab': 'AxeStone', 'mods': 'Shift'}, wait(0.4),
    H('windowOpen', True, 'Shift (the shared key) still opens Reskin'),
    RH('windowOpen', False, 'but not RenameIt, whose key is now Ctrl'),
    {'do': 'click', 'button': 'Close'}, wait(0.2), {'do': 'closeUi'},
])

linked_suite = {
    'name': 'DrakesReskinIt-Linked',
    'description': 'ReskinIt working with RenameIt: AdminSource / ExclusionSource linked to RenameIt (VIPs, exclusions, merge), renames and '
                   'reskins together, and the shared item window with each mod\'s key.',
    'requireMods': [{'guid': GUID, 'minVersion': '0.1.0'}, {'guid': RI, 'minVersion': '1.3.0'}],
    'configOverrides': suite['configOverrides'] + [{'guid': RI, 'section': k.split('/')[0], 'key': k.split('/')[1], 'value': v} for k, v in {
        **RI_BASE, '02 Features/RenameEnabled': 'true', '05 General/LockToOwner': 'true', '07 UnlockCost/UnlockCostEnabled': 'false',
        '11 UI-NotSynced/MenuOpenModifier': ''}.items()],
    'setup': suite['setup'],
    'resetBetweenTests': True,
}
if os.path.isdir(LINKED_OUT):
    shutil.rmtree(LINKED_OUT)
os.makedirs(LINKED_OUT)
with open(os.path.join(LINKED_OUT, 'suite.json'), 'w', encoding='utf-8') as f:
    json.dump(linked_suite, f, indent=2, ensure_ascii=False)
for tid, t in ltests.items():
    with open(os.path.join(LINKED_OUT, f'{tid}.json'), 'w', encoding='utf-8') as f:
        json.dump(t, f, indent=2, ensure_ascii=False)
print(len(ltests), 'linked tests')

