This mod includes a couple of differently configured character overrides and variants:

- Human: overrides the human character with a broken version whose ragdoll fails to load.
	- Expected behavior: loading the human character will fail and cause console errors, and the game will load the vanilla version instead.

- Mudraptor: overrides Mudraptor with a broken version whose ragdoll fails to load.
	- Expected behavior: loading a Mudraptor will fail and cause console errors, and the game will load the vanilla Crawler ragdoll instead.
	- Variants of Mudraptor should fail to load as well, and switch to the Crawler ragdoll instead.

- Crawler: overrides Crawler with a green version with sunglasses.
	- Expected behavior: Crawler spawns as a green version with sunglasses.
	- This change should also affect variants of Crawler: Crawler_large should also be green and have sunglasses (even though the mod does 
	not modify it directly).
	- Crawler_hatchling (variant of Crawler) should look unchanged, but load correctly (despite it being a vanilla character whose base 
	character has now been overridden by a mod).

- Testcyborgworm_m: adds a variant of the Cyborgworm (identical to the normal Cyborgworm).
	- Expected behavior: Testcyborgworm_m looks identical to Cyborgworm.
	- This has previously caused issues, because the Cyborgworm uses multiple textures, some of which aren't in the character folder, 
	and these used to load incorrectly when the character is a variant.
    - Note that the character is configured incorrectly: it's defined to be an override, but there's no character (Testcyborgworm_m) it'd override. 
    It works regardless, so this can be used as a test case for checking that these incorrectly defined characters still load.

- Spineling_morbusine_m: adds a variant of Spineling_morbusine (identical to the normal Spineling_morbusine).
	- Expected behavior: Spineling_morbusine_m looks identical to Spineling_morbusine.
	- This has previously caused issues, because Spineling_morbusine defines the ragdoll slightly differently than other monsters 
	(not in the usual Ragdoll folder, but a hard-coded path to a ragdoll file in the character's folder).