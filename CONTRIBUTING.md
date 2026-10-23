# Contribuer à Siana Bot

Siana Bot est le bot Discord de la promo **INFRES19** : tout le monde peut proposer des features ou des corrections.
Toute modification passe par une **pull request vers `main`** : personne ne pousse directement sur `main`.

## Mise en place

Prérequis : [.NET 10 SDK](https://dotnet.microsoft.com/download) et git.

1. **Fork** le repo sur GitHub, puis clone ton fork :
   ```bash
   git clone git@github.com:<ton-pseudo>/Siana.git
   cd Siana
   git remote add upstream git@github.com:ilyanfraimbault/Siana.git
   ```
2. **Crée ton propre bot de test**. On ne développe jamais avec le bot de prod.
   - [Developer Portal](https://discord.com/developers/applications) → *New Application* → onglet *Bot* → *Reset Token*.
   - Invite-le sur **ton serveur de test** (scopes `bot` + `applications.commands`, permissions *Gérer les rôles*, *Ajouter des réactions*, *Voir les salons*, *Envoyer des messages*, *Lire l'historique*).
3. **Configure tes secrets** (stockés hors du repo) :
   ```bash
   dotnet user-secrets set "Discord:Token" "<token de ton bot de test>" --project src/SianaBot
   dotnet user-secrets set "Discord:DevGuildId" "<id de ton serveur de test>" --project src/SianaBot
   ```
4. Lance le bot :
   ```bash
   dotnet run --project src/SianaBot
   ```

## Workflow

1. **Une feature un peu grosse ? Ouvre d'abord une issue** pour en discuter, ça évite de coder pour rien.
2. **Pars d'un `main` à jour** et crée une branche dans ton fork :
   ```bash
   git fetch upstream && git switch -c feat/mon-sujet upstream/main
   ```
   Préfixes de branche : `feat/`, `fix/`, `docs/`, `refactor/`, `chore/`.
3. **Commits** au format [Conventional Commits](https://www.conventionalcommits.org/fr/), en français :
   ```
   <type>(<scope optionnel>): <description courte au présent>
   ```
   | Type       | Pour                                             |
   | ---------- | ------------------------------------------------ |
   | `feat`     | une nouvelle fonctionnalité                      |
   | `fix`      | une correction de bug                            |
   | `docs`     | la documentation uniquement                      |
   | `refactor` | une restructuration sans changement de comportement |
   | `chore`    | maintenance, dépendances, configuration          |
   | `ci`       | les workflows GitHub Actions, Docker             |

   Exemples : `feat(reactionrole): limite à un rôle par message`, `fix: ignore les réactions des bots`.
4. **Avant d'ouvrir la PR** :
   ```bash
   dotnet build
   dotnet format Siana.slnx --verify-no-changes
   ```
   Et **teste à la main sur ton serveur de test**.
5. **Ouvre la PR vers `ilyanfraimbault/Siana:main`** et remplis le template.
   - Le **titre de la PR** suit aussi Conventional Commits : c'est lui qui devient le message du commit (squash merge).
   - Un sujet par PR, de préférence petite : elle sera relue plus vite.
6. Une **review de @ilyanfraimbault** est obligatoire avant le merge. Les PR sont fusionnées en *squash*.

## Conventions de code

- **Formatage** : le `.editorconfig` fait foi. `dotnet format` doit passer sans changement.
- **Une feature = un dossier** sous `src/SianaBot/<Feature>/`, sur le modèle de [`ReactionRoles/`](src/SianaBot/ReactionRoles) :
  - `<Feature>Module.cs` : les slash commands (`InteractionModuleBase`), découvertes automatiquement ;
  - `<Feature>Handler.cs` : les events de la gateway, branchés au démarrage dans `BotService` ;
  - `<Feature>Store.cs` : la persistance éventuelle ;
  - les services sont enregistrés dans `Program.cs`.
- **Slash commands** : noms en minuscules, sans accents (`/reactionrole add`). Descriptions et messages affichés en **français**.
- **Permissions** : protège les commandes d'administration avec `[DefaultMemberPermissions]`, et réponds en `ephemeral` quand le message n'intéresse que l'auteur.
- **Intents** : pas de nouvel intent privilégié (`GuildMembers`, `MessageContent`, `GuildPresences`) sans en discuter dans une issue.
- **Nullable activé, zéro warning** au build.
- **Données persistantes** : sous le dossier configuré (en prod, le volume `/data`), jamais à côté du binaire.

## Secrets et configuration

- **Ne commite jamais un token ou une clé**, même de test. Utilise `dotnet user-secrets`.
- Une nouvelle option passe par une classe d'options (`IOptions<T>`) avec une valeur par défaut dans `appsettings.json`, et elle est documentée dans le README.
- Si elle doit être définie en prod, signale-le dans la PR : il faudra l'ajouter dans `compose.prod.yaml` et dans les secrets du repo.

## Releases

Seul le mainteneur (@ilyanfraimbault) publie des releases. Les tags suivent [SemVer](https://semver.org/lang/fr/) (`vX.Y.Z`).
**Publier une release déploie automatiquement le bot en prod.** Une pre-release publie l'image sans déployer.
