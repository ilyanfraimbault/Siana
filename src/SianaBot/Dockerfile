FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SianaBot.csproj", "./"]
RUN dotnet restore "SianaBot.csproj"

COPY . .
RUN dotnet publish "SianaBot.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Volume des données persistantes (reaction roles). Le volume nommé hérite de cet owner.
RUN mkdir -p /data && chown app:app /data
ENV ReactionRoles__StoragePath=/data/reaction-roles.json
VOLUME /data

USER app
ENTRYPOINT ["dotnet", "SianaBot.dll"]
