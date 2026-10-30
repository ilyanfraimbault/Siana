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

Le rôle suit la réaction : réagir le donne, retirer sa réaction l'enlève. À chaque démarrage, le bot rattrape les réactions
posées pendant qu'il était hors ligne.

### Panneaux de rôles à boutons

`siana roles panel` publie un embed avec un bouton par rôle : un clic donne le rôle, un second le retire, et le bot
confirme par un message visible du seul membre. Avec `--exclusive`, prendre un rôle du panneau retire les autres
(le panneau des filières : on ne peut pas avoir DL et SR en même temps). Les boutons portent leur rôle, rien n'est stocké.

Les anciens panneaux à réactions (mode toggle) se convertissent sur place avec `siana roles convert --message <lien> [--exclusive]` :
le message garde son lien, ses réactions sont retirées (il faut **Gérer les messages**) et le bot cesse de les suivre.

### Convention de pseudo

Si `NicknameConvention:GuildId` et `NicknameConvention:RoleId` sont configurés, le bot donne ce rôle aux membres dont le pseudo est
`Prénom N` ou `Prénom N.`, avec 1 à 4 lettres du nom pour les noms composés (lettres uniquement, prénoms composés acceptés), et le retire sinon. Il le vérifie à l'arrivée d'un membre,
à chaque changement de pseudo et au démarrage. Il faut activer l'intent **Server Members** dans le Developer Portal.

Les rôles listés dans `NicknameConvention:ReservedRoleIds` (les filières DL et SR) sont **réservés aux membres conformes** :
un choix fait sans pseudo conforme est mis en attente et activé dès que le pseudo est corrigé ;
perdre le rôle de la convention remet ces rôles en attente. Comme les salons INFRES ne s'ouvrent qu'avec DL ou SR,
il faut à la fois un pseudo conforme et une filière pour y accéder ; sinon, seul #roles est visible.

### Logs de modération

Si `ModerationLog:GuildId` est configuré, le bot publie dans un salon réservé aux admins un embed pour chaque :

- ban, débannissement, kick, exclusion temporaire (timeout), mute ou sourdine vocale ;
- rôle ajouté ou retiré, et pseudo du serveur modifié ;
- message modifié (avant / après) ou supprimé (contenu et pièces jointes), y compris les suppressions en masse ;
- slash command du bot utilisée, avec ses arguments, le salon et l'erreur éventuelle ;
- arrivée et départ d'un membre, et changement de profil Discord (nom d'utilisateur, nom affiché, avatar) ;
- message privé reçu par le bot (réponse à un `/mp`), avec ses pièces jointes.

L'embed montre l'avatar du membre concerné, la raison éventuelle, et toujours en footer l'auteur de l'action avec son avatar
(sauf s'il est inconnu : suppression en masse, message supprimé antérieur au démarrage du bot).

Les actions de modération, rôles et pseudos sont lus dans l'audit log du serveur : ils sont journalisés qu'ils soient faits
depuis Discord, par un autre bot ou par Siana (reaction roles, convention de pseudo). Il faut au bot la permission
**Voir les logs du serveur**, et les intents privilégiés **Server Members** et **Message Content** dans le Developer Portal.

Le contenu d'origine d'un message modifié ou supprimé vient du cache du bot (200 derniers messages par salon) :
il est inconnu pour les messages envoyés avant son démarrage. Les messages des bots et du salon de logs sont ignorés.

Le salon est `ModerationLog:ChannelId` s'il est renseigné ; sinon le bot cherche `#logs-moderation` (`ModerationLog:ChannelName`)
et, s'il n'existe pas, le crée masqué à @everyone (seuls les administrateurs et le bot le voient), ce qui demande **Gérer les salons**.
Renommer ce salon sans renseigner `ChannelId` en ferait créer un nouveau au prochain démarrage.

### Messages privés

`/mp user:<membre> message:<texte>` envoie `texte` en message privé au membre, de la part du bot.
La commande est réservée par défaut aux administrateurs (ouvrable à d'autres rôles dans Paramètres du serveur → Intégrations),
et chaque envoi est journalisé (expéditeur et destinataire). Si le membre bloque les MP du serveur, l'expéditeur en est averti.
Si les logs de modération sont activés, les MP que les membres envoient au bot (leurs réponses) y apparaissent.

## Administration (CLI)

Le binaire sert aussi de CLI, qui passe par l'API REST avec le token du bot. En prod, dans le conteneur :

```bash
ssh truemain-prod docker exec siana-bot siana help
```

| Commande | Effet |
| --- | --- |
| `siana roles list` | liste les reaction roles suivis |
| `siana roles sync` | rattrape les réactions posées pendant une coupure |
| `siana roles grant --message <lien> --emoji <e> --role <id>` | donne le rôle à ceux qui ont réagi sur un message non suivi |
| `siana roles panel --channel <id> --title <t> [--description <d>] [--color <#hex>] [--exclusive] --entry "<e>\|<id rôle>\|<libellé>"...` | publie un panneau de rôles à boutons |
| `siana roles convert --message <lien> [--exclusive]` | remplace les réactions d'un ancien panneau par des boutons |
| `siana nick check <pseudo>` / `siana nick sync` | teste un pseudo / applique la convention à tout le serveur |

En local : `dotnet run --project src/SianaBot -- roles list`.
