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
