# Additional permission: the Unity runtime

Duel Circuit's code is licensed under the GNU Affero General Public License, version 3 or (at your option) any later
version ([LICENSE](../LICENSE)). The copyright holders add the permission below under section 7 of that license, so
the game can be built with Unity and shipped. [LICENSING.md](../LICENSING.md) says which files it covers.

Copyright (C) 2026 Jake Joseph (GitHub: fullstackjake) and the Duel Circuit authors.

## The permission

> **Additional permission under GNU AGPL version 3 section 7**
>
> If you modify this Program, or any covered work, by linking or combining it with the Unity Runtime (or a modified
> version of the Unity Runtime), containing parts covered by the terms of Unity Technologies' licenses for the Unity
> Runtime, the licensors of this Program grant you additional permission to convey the resulting work. Corresponding
> Source for a non-source form of such a combination need not include the source code for the parts of the Unity
> Runtime used. It shall still include the Corresponding Source of the covered work, which remains licensed under
> the GNU Affero General Public License.
>
> "Unity Runtime" means the runtime parts of the Unity engine that Unity Technologies (or its affiliates) distribute
> for inclusion in applications built with Unity: the Unity player and its engine modules, the scripting runtime and
> class libraries Unity ships with it (Mono or IL2CPP), and the packages Unity Technologies publishes under the Unity
> Companion License or its other license terms. It does not include this Program or any part of it, and it does not
> include software from anyone other than Unity Technologies.
>
> This permission is granted only by the copyright holders of the parts of this Program that carry it (the Duel
> Circuit authors), and only for those parts. It does not grant, and cannot grant, any permission for works by other
> copyright holders that are combined with or distributed alongside this Program, including ygopro-core and
> Project Ignis' CardScripts, which are licensed by their own authors under the GNU Affero General Public License
> without this permission.

## What this means, in plain words

- You may build our code into a game with Unity and share that game, even though Unity itself is not open source.
- You don't have to provide Unity's source code. You do have to provide the source code of everything under the AGPL
  that is in the game, as the AGPL says.
- The permission only covers code the Duel Circuit authors wrote. Our patches in `patches/ygopro-core/` are changes
  to ygopro-core, so they follow ygopro-core's terms, without this permission. It does not cover the rules engine
  (ygopro-core) or the card scripts (CardScripts). Only their authors can give that permission.
  We are asking edo9300 (ygopro-core) and Project Ignis (CardScripts, BabelCDB, LFLists) for permission to ship their work this way. The answer will be recorded in [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
- If you change our code, AGPL section 7 lets you remove this permission from your version. If you remove it, your
  version can no longer be shipped as a Unity build.
- Contributions to this repository are accepted under the same terms, including this permission
  ([CONTRIBUTING.md](../CONTRIBUTING.md)).

This is not legal advice.
