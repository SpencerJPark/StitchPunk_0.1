// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Runtime.CompilerServices;

// Architecture section 8 M1: internal stableId fields on the authoring assets are read by the
// Editor assembly (inspectors, id tooling) and by both test assemblies. These are the only
// contracted InternalsVisibleTo pairs in the package.
[assembly: InternalsVisibleTo("StitchPunk.AnimationToolkit.Editor")]
[assembly: InternalsVisibleTo("StitchPunk.AnimationToolkit.Tests.EditMode")]
[assembly: InternalsVisibleTo("StitchPunk.AnimationToolkit.Tests.PlayMode")]
