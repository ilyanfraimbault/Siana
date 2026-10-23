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

## Déploiement

Chaque **release publiée** sur GitHub déclenche [`deploy.yml`](.github/workflows/deploy.yml) :
build de l'image `ghcr.io/ilyanfraimbault/siana-bot:<tag>` (+ `latest`), puis redéploiement du projet
Docker Manager `siana` sur le VPS Hostinger avec [`compose.prod.yaml`](compose.prod.yaml).
Une pre-release publie l'image (tag `prerelease`) sans déployer.

Configuration du repo GitHub (Settings → Secrets and variables → Actions) :

| Type     | Nom                 | Valeur                                              |
| -------- | ------------------- | --------------------------------------------------- |
| variable | `HOSTINGER_VM_ID`   | id du VPS dans l'API Hostinger                      |
| secret   | `HOSTINGER_API_KEY` | token API du compte Hostinger (hPanel → Compte → API) |
| secret   | `DISCORD_TOKEN`     | token du bot                                        |

L'image est privée : le VPS doit être connecté à GHCR (`docker login ghcr.io`) avec un token `read:packages`.
Les reaction roles sont stockés dans le volume Docker `siana-data`.

## Fonctionnalités

### Reaction role

Commandes réservées aux membres ayant **Gérer les rôles** :

- `/reactionrole add message:<id ou lien> emoji:<emoji> role:<rôle>` — le bot ajoute la réaction, et toute personne qui réagit reçoit le rôle (retiré si elle enlève sa réaction).
- `/reactionrole remove message:<id ou lien> emoji:<emoji>`
- `/reactionrole list`

Le rôle du bot doit être placé **au-dessus** des rôles qu'il attribue. Les associations sont stockées dans `data/reaction-roles.json`.
