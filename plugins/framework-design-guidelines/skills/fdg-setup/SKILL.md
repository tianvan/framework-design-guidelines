---
name: fdg-setup
description: >-
  Set up Framework Design Guidelines checks in this repository: add the FrameworkDesignGuidelines.Analyzers package,
  choose a profile (library, application or test) for each directory, build once to confirm the rules are active,
  and install the dotnet fdg CLI. Use when the user runs /fdg-setup (or `$fdg-setup` on Codex) or asks to set up or
  repair FDG.
disable-model-invocation: true
---

# Set up Framework Design Guidelines

Onboard this repository's C# projects in four steps. Ground rules for every step:

- Change only files in the working tree. Never run `git add`, `git commit`, `git stash` or `git checkout`, and never
  create a branch. The user reviews the result with `git diff` and commits it.
- Before you edit anything, list every file you will create or change, with what you will put in it, and wait for
  the user to confirm. Edit nothing until they do.
- Add no plugin-specific files at the repository root. Settings go into the project's own MSBuild files.
- Never put the package id in comments: the hooks would read a commented-out reference as onboarding.

## Step 1: Find the projects

1. List the project files: `git ls-files "*.csproj"` plus untracked ones (`git ls-files --others --exclude-standard
   "*.csproj"`); outside git, search the directory tree.
2. A project is SDK-style when its root element is `<Project Sdk="...">`, or it contains `<Sdk Name="..." />` or
   `<Import ... Sdk="..." />`. Only SDK-style projects are supported. Name every other (legacy) project and tell the
   user it is not supported and will be left untouched.
3. Run `dotnet --version`. FDG needs .NET SDK 8.0.100 or later; if it is older, say so and stop.
4. Note what already exists: every `Directory.Build.props`, a `Directory.Packages.props` with
   `ManagePackageVersionsCentrally` set to `true` (central package management), and any project that already
   references `FrameworkDesignGuidelines.Analyzers` or sets `FrameworkDesignGuidelinesProfile`. Keep what is right.

## Step 2: Choose a profile for each directory

The MSBuild property `FrameworkDesignGuidelinesProfile` selects the rule set:

| Profile | For | Effect |
|---|---|---|
| `library` | Libraries published for others to use | Strictest: API design rules for the public surface, plus versioning and compatibility rules |
| `application` | Application code, including every DDD layer | API design rules for all types and members, no binary compatibility checks |
| `test` | Test projects | Chapter 3 rules are off (`Method_WhenX_DoesY` is fine), API design rules off |

Recommend a profile per directory and ask the user to confirm or change it:

- `tests/`, and projects named `*.Tests` or referencing a test framework: `test`.
- Projects published as packages for others (an explicit `PackageId`, or pushed to a feed): `library`. Do not go
  by `IsPackable`; SDK class libraries are packable by default.
- Everything else: `application`.

The recommended layout is one `Directory.Build.props` per directory, for example `src/Directory.Build.props` with
`application` (or `library`) and `tests/Directory.Build.props` with `test`. A project that differs from its directory
sets the property in its own `.csproj`.

For each `library` directory, also ask whether to enable PublicApiAnalyzers
(`Microsoft.CodeAnalysis.PublicApiAnalyzers`), which tracks public API changes in `PublicAPI.Shipped.txt` and
`PublicAPI.Unshipped.txt`. The default is no.

## Step 3: Add the package and the profiles

First show the user the list of files you will change, then wait for confirmation (see the ground rules).

1. Find the latest version of `FrameworkDesignGuidelines.Analyzers` with
   `dotnet package search FrameworkDesignGuidelines.Analyzers --exact-match`. If that fails, ask the user.
2. Reference the package, always with `PrivateAssets="all"` (it is a development dependency, so its rules never
   flow to the library's consumers):
   - With central package management, put the version in `Directory.Packages.props` only, and leave it off the
     reference:

     ```xml
     <PackageVersion Include="FrameworkDesignGuidelines.Analyzers" Version="X.Y.Z" />
     ```

     ```xml
     <PackageReference Include="FrameworkDesignGuidelines.Analyzers" PrivateAssets="all" />
     ```

   - Without it, the reference carries the version:

     ```xml
     <PackageReference Include="FrameworkDesignGuidelines.Analyzers" Version="X.Y.Z" PrivateAssets="all" />
     ```

3. Write the profile and the reference into each directory's `Directory.Build.props`. Add to an existing file and
   keep its content. A new file must import the next `Directory.Build.props` above it, if there is one, because
   MSBuild stops at the first one it finds. With central package management:

   ```xml
   <Project>
     <PropertyGroup>
       <ParentDirectoryBuildProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</ParentDirectoryBuildProps>
     </PropertyGroup>
     <Import Project="$(ParentDirectoryBuildProps)" Condition="'$(ParentDirectoryBuildProps)' != ''" />

     <PropertyGroup>
       <FrameworkDesignGuidelinesProfile>application</FrameworkDesignGuidelinesProfile>
     </PropertyGroup>

     <ItemGroup>
       <PackageReference Include="FrameworkDesignGuidelines.Analyzers" PrivateAssets="all" />
     </ItemGroup>
   </Project>
   ```

   Without central package management, the same file with the version on the reference:

   ```xml
       <PackageReference Include="FrameworkDesignGuidelines.Analyzers" Version="X.Y.Z" PrivateAssets="all" />
   ```

   If a directory also holds legacy projects, put the `PackageReference` in each SDK-style `.csproj` instead: legacy
   projects import `Directory.Build.props` too, and a `PackageReference` there would change how they restore.
4. If the user enabled PublicApiAnalyzers for a library, reference `Microsoft.CodeAnalysis.PublicApiAnalyzers` the same
   way (`PrivateAssets="all"`, version in `Directory.Packages.props` under central package management) in that
   library's `Directory.Build.props` or `.csproj`, and create empty `PublicAPI.Shipped.txt` and
   `PublicAPI.Unshipped.txt` next to each library project. The package picks these files up itself; do not add them
   as `AdditionalFiles`. Tell the user that RS0016 now reports every public API not yet recorded, and that its code
   fix adds them to `PublicAPI.Unshipped.txt`.

## Step 4: Build and confirm

Work through this list in order; do not skip a project.

1. Write down the onboarded projects: the SDK-style projects from step 1 that are in a directory the user chose a
   profile for, or that reference the package in their own `.csproj`.
2. Build them all (`dotnet build` restores first):
   1. List the solutions: `git ls-files "*.sln" "*.slnx"` plus untracked ones, as in step 1.
   2. For each solution, list its projects with `dotnet sln <solution> list`. Build every solution that contains at
      least one onboarded project: `dotnet build <solution>`.
   3. Build every onboarded project that is in no solution on its own: `dotnet build <the .csproj>`.
3. For each onboarded project, one by one, open `obj/fdg/marker.json` in the project directory. Write down every
   project whose marker is missing, or whose `profile` is not the one chosen, and every project whose build showed
   FDG0001 (profile not set) or FDG0002 (unknown profile). For each, fix the cause (the `PackageReference`, the
   `FrameworkDesignGuidelinesProfile` property, or the import of the `Directory.Build.props` above), build that
   project again, and check its marker again. Go on until no project is left on the list.
4. Existing code may now show FDG, CA and IDE warnings. That is expected: the end-of-turn check reports only the
   lines the AI changes, and `/fdg-fix` cleans up a directory when the user wants it.
5. Summarize the files you changed and remind the user to review them with `git diff` and commit them.
6. Check the CLI that the hooks and `/fdg-fix` use: run `dotnet-fdg --version` (the same tool as `dotnet fdg`;
   its own executable starts without resolving an SDK). If the shell cannot find `dotnet-fdg` ("command not found",
   "is not recognized"), and `~/.dotnet/tools/dotnet-fdg` does not exist either, it is not installed: tell the user
   to install it, then restart Claude Code or Codex:

   ```
   dotnet tool install -g dotnet-fdg
   ```

   An error that says "A compatible .NET SDK was not found" or names a requested SDK version is not a missing tool:
   the repository's `global.json` pins an SDK that is not installed. Tell the user to install that SDK or relax
   `rollForward`; do not suggest installing dotnet-fdg.

   On Codex, the plugin's hooks also have to be approved once in `/hooks`, and again after an update changes them;
   until then they silently do nothing.
