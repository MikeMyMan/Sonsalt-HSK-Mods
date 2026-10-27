"""Single source of truth for the gases. gen_defs.py and gen_sprites.py read this.

Nine gases take over one of the Gas Pack's slots each (their def names stay the same); the two farm
chemicals (agent orange, verminbane) have their own PGR_ def names. Every gas gets a research tier
that matches its lethality and complexity, a colour, recipe costs and descriptions.
Prices: canister value is about 1.2x its ingredients at HSK prices; every delivery item is priced
from the canister it contains (see gen_defs.py).
"""

from dataclasses import dataclass, field

HSK = "skyarkhangel.HSK"


@dataclass
class Slot:
    """Def names for one gas. The nine weapons use the Gas Pack's names, so a colony can swap the Gas Pack
    for this mod and keep its items."""
    gas: str
    canister: str
    trap: str
    vent: str
    shell: str
    bullet: str
    implant: str = None          # suffix of PGP_GasTrapImplant<X>, for the gases with a trap implant


@dataclass
class Gas:
    key: str                     # used in new def names and texture paths
    label: str                   # lower case, used in sentences
    short: str                   # capitalised, used in CE ammo labels: "81mm mortar shell (Short)"
    slot: Slot
    research: str
    tier: str                    # shown in descriptions
    color: tuple                 # RGB 0..1, same as the gas overlay
    glyph: str                   # symbol painted on grenades and shells
    canister_cost: dict
    canister_value: float
    canister_work: int
    lethality: str
    complexity: str
    lore: str
    effects: str
    nanite: bool = False         # crafted at the fabrication bench instead of the drug lab
    skills: dict = field(default_factory=lambda: {"Crafting": 4, "Intellectual": 5})
    vent_extra: dict = field(default_factory=dict)
    implant_value: float = 0
    stripe: tuple = None         # band colour on grenades and shells, if not the gas colour (agent orange's drums)


SLOTS = {
    "Toxic": Slot("PGP_ToxicGas", "PGP_Canister_ToxicGas", "PGP_GasTrap_Toxic", "PGP_ToxicGasVent",
                  "PGP_Shell_ToxicGas", "PGP_Bullet_ToxicGasShell", implant="Toxic"),
    "Tear": Slot("PGP_TearGas", "PGP_Canister_TearGas", "PGP_GasTrap_TearGas", "PGP_TearGasVent",
                 "PGP_Shell_TearGas", "PGP_Bullet_TearGasShell"),
    "Antibug": Slot("PGP_AntibugGas", "PGP_Canister_AntibugGas", "PGP_GasTrap_Antibug", "PGP_AntibugGasVent",
                    "PGP_Shell_AntibugGas", "PGP_Bullet_AntibugGasShell"),
    "Rage": Slot("PGP_RageGas", "PGP_Canister_RageGas", "PGP_GasTrap_Rage", "PGP_RageGasVent",
                 "PGP_Shell_RageGas", "PGP_Bullet_RageGasShell", implant="Rage"),
    "Sleep": Slot("PGP_SleepGas", "PGP_Canister_SleepGas", "PGP_GasTrap_Sleep", "PGP_SleepGasVent",
                  "PGP_Shell_SleepGas", "PGP_Bullet_SleepGasShell"),
    "Fear": Slot("PGP_FearGas", "PGP_Canister_FearGas", "PGP_GasTrap_Fear", "PGP_FearGasVent",
                 "PGP_Shell_FearGas", "PGP_Bullet_FearGasShell"),
    "Acid": Slot("PGP_AcidGas", "PGP_Canister_AcidGas", "PGP_GasTrap_Acid", "PGP_AcidGasVent",
                 "PGP_Shell_AcidGas", "PGP_Bullet_AcidGasShell", implant="Acid"),
    "DefNanite": Slot("PGP_DefNaniteGas", "PGP_Canister_DefNaniteGas", "PGP_GasTrap_DefNanite", "PGP_DefNaniteGasVent",
                      "PGP_Shell_DefNaniteGas", "PGP_Bullet_DefNaniteGasShell"),
    "MechVirus": Slot("PGP_MechVirusGas", "PGP_Canister_MechVirusGas", "PGP_GasTrap_MechVirus", "PGP_MechVirusGasVent",
                      "PGP_Shell_MechVirusGas", "PGP_Bullet_MechVirusGasShell"),
    # The farm chemicals have their own names.
    "Herbicide": Slot("PGR_HerbicideGas", "PGR_Canister_HerbicideGas", "PGR_GasTrap_Herbicide", "PGR_HerbicideGasVent",
                      "PGR_Shell_HerbicideGas", "PGR_Bullet_HerbicideGasShell"),
    "Pesticide": Slot("PGR_PesticideGas", "PGR_Canister_PesticideGas", "PGR_GasTrap_Pesticide", "PGR_PesticideGasVent",
                      "PGR_Shell_PesticideGas", "PGR_Bullet_PesticideGasShell"),
}

# Ordered by research tier.
GASES = [
    Gas(key="AgentOrange", label="agent orange", short="Agent Orange", slot=SLOTS["Herbicide"],
        research="PGR_Agrochemicals", tier="agrochemical aerosols", color=(0.60, 0.42, 0.24), stripe=(0.95, 0.50, 0.08),
        glyph="leaf",
        canister_cost={"Chemfuel": 6, "Neutroamine": 1}, canister_value=24, canister_work=900,
        skills={"Crafting": 3, "Intellectual": 3},
        lethality="0.5/5", complexity="1/5",
        lore="A jungle-war defoliant, named for the orange band painted on its drums. Farmers clear fields with it; "
             "soldiers strip the brush that raiders hide in and the dry grass that carries fire up to the walls. "
             "What it leaves in the body stays for years.",
        effects="- Grass, bushes, crops and flowers wither and die within a few seconds.\\n"
                "- Trees drop their leaves at once but only die after two to three minutes in thick mist.\\n"
                "- Trees with special powers (anima, gauranlen and the like) are spared.\\n"
                "- Mildly toxic: a minute in thick mist gives people and animals light toxic buildup, small animals "
                "a little more. Toxic buildup lingers for days and, built up over many exposures, can bring "
                "dementia or cancer.\\n"
                "Breathed in: a gas mask stops it completely; toxic resistance reduces the dose."),
    Gas(key="Verminbane", label="verminbane", short="Verminbane", slot=SLOTS["Pesticide"],
        research="PGR_Agrochemicals", tier="agrochemical aerosols", color=(0.20, 0.72, 0.48), glyph="bug",
        canister_cost={"Chemfuel": 4, "SmokeleafLeaves": 6, "Neutroamine": 1}, canister_value=30, canister_work=900,
        skills={"Crafting": 3, "Intellectual": 3},
        lethality="1/5, 5/5 against vermin", complexity="1/5",
        lore="Nicotine salts and cheap nerve poisons, sold by the drum to farms plagued by rats and megascarabs. Every "
             "breath carries the same dose; a small body just has less room for it.",
        effects="Living creatures only. The smaller the body, the faster it works.\\n"
                "- Rats, squirrels and other vermin die within a few seconds, megascarabs and other small insects "
                "within about five.\\n"
                "- Cats and dogs sicken over a minute or two; megaspiders and other big insects die in about forty "
                "seconds.\\n"
                "- People get nausea and tremors in the first minute and convulsions in the second; about three minutes "
                "in thick fog can kill.\\n"
                "- Muffalo and other large animals barely notice.\\n"
                "- Its rat-poison blood thinner makes every wound bleed harder for days. After about 40 seconds in thick "
                "fog the body starts bleeding inside 2-4 hours later, and keeps bleeding until a doctor treats it.\\n"
                "- Insect hives in the fog slowly die.\\n"
                "Breathed in: a gas mask stops it completely. General toxin resistance doesn't help; it is made for "
                "creatures that shrug off other poisons."),
    Gas(key="Barbsmoke", label="barbsmoke", short="Barbsmoke", slot=SLOTS["Tear"],
        research="PGP_WeaponizedGas", tier="weaponized gas", color=(0.88, 0.18, 0.25), glyph="zigzag",
        canister_cost={"Chemfuel": 4, "SmokeleafLeaves": 8, "Neutroamine": 1}, canister_value=35, canister_work=1200,
        lethality="2/5", complexity="3/5",
        lore="Microscopic crystalline barbs carried in a pink-grey haze. It is not breathed: the barbs work into the skin "
             "through seams, cuffs and collars and set off every nerve they touch.",
        effects="- Pain spikes within a second; most pawns drop into pain shock.\\n"
                "- Movement is capped at 30% and manipulation at 20% while the spasms last.\\n"
                "- Chemical burns on the limbs, hands and neck that can blister and turn septic.\\n"
                "Gas masks do nothing. Only a sealed suit that covers torso and legs and protects against toxins "
                "(such as a chemical protection suit) keeps it out."),
    Gas(key="Dreadspore", label="dreadspore vapor", short="Dreadspore", slot=SLOTS["Fear"],
        research="PGP_WeaponizedGas", tier="weaponized gas", color=(0.88, 0.22, 0.72), glyph="spores",
        canister_cost={"Chemfuel": 3, "PsychoidLeaves": 12, "Neutroamine": 2}, canister_value=50, canister_work=1200,
        lethality="1.5/5", complexity="3/5",
        lore="Spores of a cave fungus, bred until a few breaths flood the brain with terror. It does no harm by itself; "
             "the harm comes from the people breathing it.",
        effects="Living creatures only.\\n"
                "- Shooting accuracy drops by 80% at once.\\n"
                "- After about 6 seconds the mind snaps, whatever the mood: brawlers, bloodlusters and psychopaths go "
                "berserk and attack whoever is closest; everyone else drops what they carry and flees.\\n"
                "- No lasting harm once it wears off.\\n"
                "Breathed in: a gas mask stops it completely."),
    Gas(key="GearRot", label="gear-rot fume", short="Gear-rot", slot=SLOTS["Rage"],
        research="PGR_CorrosiveAgents", tier="corrosive agents", color=(0.82, 0.42, 0.12), glyph="gear",
        canister_cost={"Chemfuel": 6, "Neutroamine": 3, "Silver": 10}, canister_value=55, canister_work=1600,
        lethality="3/5", complexity="4/5",
        lore="The answer to raiders who stroll through gas lines behind sealed masks. Gear-rot ignores the wearer and "
             "eats the kit: fabric, polymer, ceramic plate and steel pit and crumble in minutes.",
        effects="- In thick fume, every worn item loses 2-5% of its hit points per second.\\n"
                "- Masks, helmets and armor that reach zero fall apart, leaving the wearer open to whatever else is in "
                "the air.\\n"
                "- Uncovered skin takes acid burns.\\n"
                "Mix it with a lethal gas.",
        implant_value=650),
    Gas(key="Scourge", label="scourge fog", short="Scourge", slot=SLOTS["Acid"],
        research="PGR_CorrosiveAgents", tier="corrosive agents", color=(0.92, 0.84, 0.12), glyph="drop",
        canister_cost={"Chemfuel": 8, "Neutroamine": 4, "Uranium": 2}, canister_value=65, canister_work=1600,
        lethality="2.5/5", complexity="4/5",
        lore="A heavy, clinging acid for ground you never want back. Nothing worth salvaging survives where it "
             "settles, and it does not care what you are wearing.",
        effects="- Kills every plant it touches: crops, grass and trees.\\n"
                "- Dissolves weapons, apparel, corpses and resources lying on the ground.\\n"
                "- Eats doors, barricades, traps, conduits and walls. Wood goes in seconds, steel in under a minute, "
                "stone and plasteel hold out far longer.\\n"
                "- Burns skin straight through ordinary clothes and armor. A gas mask shields only the face and lungs; "
                "a chemical suit only the body. Only both together keep it off.\\n"
                "- Breathed without a mask it burns the lungs. Half a minute in thick fog and the lungs fill with fluid "
                "over the next hours: fatal unless a doctor treats it. Badly burned lungs scar for good.\\n"
                "Raiders path around scourge fog unless sapping orders drive them through.",
        implant_value=700),
    Gas(key="Slumbercloud", label="slumbercloud", short="Slumbercloud", slot=SLOTS["Sleep"],
        research="PGR_SedativeAerosols", tier="sedative aerosols", color=(0.25, 0.45, 0.95), glyph="moon",
        canister_cost={"Chemfuel": 3, "Neutroamine": 2, "MedicineHerbal": 3, "SmokeleafLeaves": 10},
        canister_value=75, canister_work=1600,
        lethality="3.5/5", complexity="2/5",
        lore="A fast sedative aerosol from colonial police stocks. It takes the fight out of a room without leaving "
             "bodies to bury.",
        effects="Living creatures only.\\n"
                "- About 4 seconds: the pawn collapses into a deep sleep and drops its weapon.\\n"
                "- Carried into clean air, they wake several hours later with no lasting harm. Ideal for taking "
                "prisoners and harvesting bionics.\\n"
                "- Left lying in thick cloud for 8 seconds, the sleep becomes an anoxic coma that kills after 10 more "
                "seconds. Pulled out of the coma alive, the pawn keeps brain scarring.\\n"
                "Breathed in: a gas mask stops it completely; toxic resistance reduces the dose."),
    Gas(key="Hexvein", label="hexvein mist", short="Hexvein", slot=SLOTS["Antibug"],
        research="PGR_NerveAgents", tier="nerve agents", color=(0.55, 0.20, 0.90), glyph="hex",
        canister_cost={"Chemfuel": 4, "Neutroamine": 5, "MedicineIndustrial": 2, "PsychoidLeaves": 6},
        canister_value=105, canister_work=2000, skills={"Crafting": 5, "Intellectual": 7},
        vent_extra={"Plasteel": 10},
        lethality="4.5/5", complexity="2/5",
        lore="Named for the black, branching bruises it leaves under the skin. Hexvein jams the junctions between nerve "
             "and muscle in anything with a nervous system: people, animals and insectoids alike.",
        effects="Living creatures only; mechanoids are immune.\\n"
                "- About 3 seconds: tremors (aiming delay +150%, movement -40%).\\n"
                "- About 6 seconds: convulsions, vomiting, extreme pain, no manipulation.\\n"
                "- 10 to 12 seconds: the breathing muscles stop.\\n"
                "- Survivors suffer 15 to 30 days of neuro-ataxia. Those who went through convulsions keep permanent "
                "nerve damage.\\n"
                "It soaks through skin as well as lungs: a gas mask alone only slows it to about half a minute. A "
                "chemical suit and a gas mask together stop it."),
    Gas(key="Deathbreath", label="deathbreath fog", short="Deathbreath", slot=SLOTS["Toxic"],
        research="PGR_TerminalAsphyxiants", tier="terminal asphyxiants", color=(0.50, 0.85, 0.15), glyph="cross",
        canister_cost={"Chemfuel": 6, "Neutroamine": 8, "MedicineIndustrial": 2, "Uranium": 3},
        canister_value=140, canister_work=2400, skills={"Crafting": 6, "Intellectual": 9},
        vent_extra={"Plasteel": 10},
        lethality="5/5", complexity="2/5",
        lore="Developed for trench wars on a world with nobody left to remember them. An odourless asphyxiant that "
             "shuts a body down in stages: the brain, then the lungs, then the heart.",
        effects="Living creatures only; mechanoids are immune.\\n"
                "- About 2 seconds in dense fog: collapse.\\n"
                "- About 5 seconds: respiratory and cardiac arrest.\\n"
                "- Pawns pulled out right after collapsing live, but usually with hypoxic brain scarring or myocardial "
                "necrosis that only glitterworld surgery, a biosculpter or a healer mech serum can repair.\\n"
                "- After about 3 seconds the dose is lethal: deathbreath poisoning keeps spreading after the pawn is out "
                "and kills within hours unless a doctor treats it.\\n"
                "Breathed in: a gas mask stops it completely; toxic resistance reduces the dose.",
        implant_value=1200),
    Gas(key="Aegis", label="aegis swarm", short="Aegis", slot=SLOTS["DefNanite"],
        research="PGR_IFFNaniteSwarms", tier="IFF nanite swarms", color=(0.80, 0.85, 0.90), glyph="shield",
        canister_cost={"Plasteel": 8, "ComponentIndustrial": 2, "Silver": 15, "Gold": 2},
        canister_value=210, canister_work=3000, nanite=True, skills={"Crafting": 8, "Intellectual": 6},
        vent_extra={"Plasteel": 15, "ComponentSpacer": 1},
        lethality="3.5/5", complexity="4/5",
        lore="Not a gas at all but billions of drifting micro-drones keyed to faction transponders. Safe to vent into "
             "your own corridors during a breach.",
        effects="- Colonists, allies, neutral visitors and tame animals are recognized and left alone.\\n"
                "- Anything hostile is sliced across its outer body: heavy bleeding, pain, slowed movement. Some drones "
                "slip through the cuts to the lungs, liver, kidneys and stomach, and those wounds bleed inside.\\n"
                "- Gas masks and toxic resistance give no protection; armor does.\\n"
                "- Downed enemies are left alone, so prisoners can be taken."),
    Gas(key="Cypherphage", label="cypherphage cloud", short="Cypherphage", slot=SLOTS["MechVirus"],
        research="PGR_CypherphageNanites", tier="cypherphage nanites", color=(0.15, 0.85, 0.90), glyph="chip",
        canister_cost={"Plasteel": 10, "ComponentSpacer": 1, "ComponentIndustrial": 2, "Gold": 4},
        canister_value=470, canister_work=4000, nanite=True, skills={"Crafting": 10, "Intellectual": 8},
        vent_extra={"Plasteel": 15, "ComponentSpacer": 1},
        lethality="4/5 against mechanoids, 0/5 against the living", complexity="5/5",
        lore="A nanite plague for mechanoid logic cores. It rewrites firmware at random, and now and then the result "
             "is loyal.",
        effects="Mechanoids only; harmless to living creatures.\\n"
                "Every 3 seconds a mechanoid spends in thick cloud, it rolls one malfunction:\\n"
                "- 40% loop stun for 4-6 seconds\\n"
                "- 25% component meltdown: fire, and a ruptured reprocessor that leaks chemfuel and explodes when the "
                "mechanoid dies\\n"
                "- 20% sensors blind for 15 seconds\\n"
                "- 10% permanent subroutine scramble: it attacks everything around it\\n"
                "- 5% firmware hijack: the mechanoid joins your colony"),
]

# Gas released by each delivery method: (initial amount, initial radius, pulses, pulse amount, pulse radius)
PAYLOAD = {
    "canister": (150, 1.1),
    "trap": (255, 4.9),
    "implant": (255, 3.0),
    "grenade": (220, 2.4, 2, 100, 1.5),
    "launcher": (180, 1.9, 1, 80, 1.2),
    "mortar": (255, 5.9, 4, 160, 2.5),
}

# HSK market values used to sanity-check canister prices.
INGREDIENT_VALUE = {
    "Chemfuel": 2.3, "Neutroamine": 6, "PsychoidLeaves": 1.9, "SmokeleafLeaves": 1.6, "MedicineHerbal": 10,
    "MedicineIndustrial": 18, "Plasteel": 9, "ComponentIndustrial": 32, "ComponentSpacer": 200, "Silver": 1,
    "Gold": 10, "Uranium": 6, "Steel": 1.9,
}
