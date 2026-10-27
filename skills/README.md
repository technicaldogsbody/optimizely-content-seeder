# Skills

`seed-definition/SKILL.md` teaches an AI coding assistant to write an `ISeedDefinition` or `IAiSeedDefinition` for
TechnicalDogsbody.Optimizely.ContentSeeder: the DSL (`SeedTree`, `BlockDefinition`, `SeedNode`, Visual Builder
`SectionDefinition`s), the typed AI writing brief, and the mistakes that are easy to make.

It is written once, in the open Agent Skills format. Claude Code reads that file as it is; `install.ps1` re-wraps the
same body for the other tools so they never drift apart.

```powershell
./install.ps1 -Target <your site> -Tool claude,copilot,cursor,windsurf,cline,codex,gemini   # or -Tool all
```

See the [main README](../README.md#ai-coding-assistant-skill) for where each tool's file goes. To change the skill,
edit `seed-definition/SKILL.md` only, then re-run the installer in your projects.
