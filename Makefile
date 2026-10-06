API := src/JobHunt.Api

.PHONY: run watch build test backup migration up down db

run:
	dotnet run --project $(API)

watch:
	dotnet watch --project $(API)

build:
	dotnet build

test:
	dotnet test

# jobhunt.db holds real data; -wal/-shm can hold recent writes not yet in the main file.
backup:
	@ts=$$(date +%Y%m%d-%H%M%S); for f in $(API)/jobhunt.db $(API)/jobhunt.db-wal $(API)/jobhunt.db-shm; do \
		[ -f $$f ] && cp $$f $(API)/jobhunt.$$ts.db.bak$${f##*jobhunt.db} && echo "backed up $$f"; done; true

# make migration name=AddApplications (backs up first: migrations apply on the next startup)
migration: backup
	dotnet ef migrations add $(name) --project $(API)

up:
	docker compose up -d --build

down:
	docker compose down

# Browse the dev DB at http://127.0.0.1:8081 (localhost tries ::1 first, which
# Docker Desktop + WSL mirrored networking does not forward)
db:
	docker run --rm --user $$(id -u):$$(id -g) -p 8081:8080 -v $(CURDIR)/$(API):/data -e SQLITE_DATABASE=jobhunt.db coleifer/sqlite-web
