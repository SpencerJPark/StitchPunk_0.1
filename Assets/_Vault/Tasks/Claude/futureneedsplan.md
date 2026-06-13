needed systems

soundsystemgroup
how this works is sound entities will be spawned in the frame (similar to my log system)
the goal of this system is to read all those entities and mix their sounds
sounds will change based on proximity to camera (range will be larger since you can switch from controlling the main character to a god mode fly over when you are controlling your minions
Sounds will be tied to animations, spawned by effects, ambient in the world, ect… some last forever on a loop, some spawn, play, and then stop and despawn or are recycled entities

Dialoguesystemgroup
Has not been tested yet, is also missing the ui that corresponds to it

SaveSystem
Somehow I need to be able to save data from data components. This is more than just player, since minions are unique I’d like them to have some permanence in their design. This also will involve an auto save that is time and travel based, along with a manual save in the menu ui

BuildingSystem
Player is able to build buildings and structures for storing resources or producing this which allows them to expand their inventory storage for wood, scrap metal, corpses, ect… when they are setting up a base for an attack on the enemy.

Player resource system
A system/entity that tracks the players inventory and resources

Game Ui

Menu Ui

Build out interactions and behaviors bulk, use Ai to assist. I will create the object models and animations, then have Ai help set up the scriptable object side. This will include resource harvesting, running machines in factory’s, picking up items, creating items and placing them on specific targets,

Trade system group
Resource and minion trade will span across the land. You will receive letters asking for orders, and you can choose to fulfill them. If you do you will have a time limit to procure and deliver what is requested. Eventually this can be automated away with a proper factory set up and distribution system. 

Vehicle system
Players and certain units can have the driving component that allows them to use vehicles. They can mount various vehicles and ride them around. For units vehicles will have their own wander waypoints, this is to help them stay on roads, if knocked off a road they can get back on them. The player will have their traveling workshop caravan as their main vehicle that they can customize. This is an important aspect to the game because this is often your base/starting point when you decide to enter a fight where you are using your minions. It can store corpses, wood, and other resources needed to produce minions. You park it and then can summon more complicated minions, control them further out thanks to upgrades, and store resources your inventory can’t hold. 

DirectionSystem
This will be part of animation, I will need to explore the best way to support characters with multiple directions they can face. Is it a model swap or more? 

