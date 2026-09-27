API := src/JobHunt.Api

.PHONY: run watch build test migration up down db

run:
	dotnet run --project $(API)

watch:
	dotnet watch --project $(API)

build:
	dotnet build

test:
	dotnet test

# make migration name=AddApplications
migration:
	dotnet ef migrations add $(name) --project $(API)

up:
	docker compose up -d --build

down:
	docker compose down

# Browse the dev DB at http://127.0.0.1:8081 (localhost tries ::1 first, which
# Docker Desktop + WSL mirrored networking does not forward)
db:
	docker run --rm --user $$(id -u):$$(id -g) -p 8081:8080 -v $(CURDIR)/$(API):/data -e SQLITE_DATABASE=jobhunt.db coleifer/sqlite-web
