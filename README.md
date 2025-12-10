# Docker-
project docker

Pour la partie worker, il faut recuperer le docker dotnet fait par microsoft

ils ont leur propre site pour stocker leur images de docker

en regardant la documentation et les contraintes, pour avoir le docker avec le tag approprie il faut recuperer mcr.microsoft.com/dotnet/sdk:7.0

https://github.com/dotnet/dotnet-docker/blob/main/README.sdk.md

Ensuite pour apprehender comment dockeriser le projet actuel, il a suffit de suivre le tutorial pour conteneuriser un projet

https://learn.microsoft.com/en-us/dotnet/core/docker/build-container?tabs=linux&pivots=dotnet-8-0


apres reflection, on peut tester le tag 7.0-alpine pour voir si le docker fonctionne avec, alpine est une image tres legere
