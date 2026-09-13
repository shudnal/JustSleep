# 1.0.8
* Added automatic sleep while sitting by a fire after continuously looking at the same burning fireplace for 20 seconds. Looking away resets the sleep preparation.
* Added a subtle sleeping head/breathing animation
* Added `Zzzzz...` overhead text for sleeping players when both clients have JustSleep installed. Vanilla players are compatible but there will be no text for them.
* Refined screen fading and sleep-transition timing for smoother visual transitions.

# 1.0.7
* Updated for the Valheim 1.0.7 release.
* Migrated configuration registration and synchronization to the standalone ConditionalConfigSync dependency.
* Updated required dependencies to BepInExPack Valheim 5.4.2350 and ConditionalConfigSync 1.0.5.
* Fixed temporary bed interaction state leaking after exceptions and guarded missing loading-screen UI elements.

# 1.0.6
* visual and text indicator for resting progress (sleep preparation)
* screen will blacken when you're sleeping

# 1.0.5
* Ashlands

# 1.0.4
* more strict check for sleep in front of the fire
* sleeping in front of the fire will be stopped if sleep requirements are not met (you are sensed by monsters or you are wet or it's daytime)

# 1.0.3
* sleep in front of the fire

# 1.0.2
* fix for sleep in bed owned by another person

# 1.0.1
* minor refinements

# 1.0.0
* Initial release