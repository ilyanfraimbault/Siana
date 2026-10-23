# Siana Bot

Bot Discord en C# (.NET 10, [Discord.Net](https://github.com/discord-net/Discord.Net)).

## Configuration

Les identifiants ne sont jamais commités. En dev, ils vivent dans les user secrets du projet :

```bash
cd src/SianaBot
dotnet user-secrets set "Discord:Token" "<token du bot>"
dotnet user-secrets set "Discord:DevGuildId" "<id du serveur>"   # optionnel : commandes instantanées sur ce serveur
dotnet user-secrets list
```

En prod (VPS), passer par des variables d'environnement : `Discord__Token`, `ReactionRoles__StoragePath`, etc.

## Lancer

```bash
dotnet run --project src/SianaBot
```

## Fonctionnalités

### Reaction role

Commandes réservées aux membres ayant **Gérer les rôles** :

- `/reactionrole add message:<id ou lien> emoji:<emoji> role:<rôle>` — le bot ajoute la réaction, et toute personne qui réagit reçoit le rôle (retiré si elle enlève sa réaction).
- `/reactionrole remove message:<id ou lien> emoji:<emoji>`
- `/reactionrole list`

Le rôle du bot doit être placé **au-dessus** des rôles qu'il attribue. Les associations sont stockées dans `data/reaction-roles.json`.
