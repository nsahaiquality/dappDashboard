# Documentation

Start with the document that matches your question.

| If you want to… | Read |
|---|---|
| Present the dashboard to management | Management overview (shared document, linked on SCRUM-74) |
| Understand the system at a glance | [architecture.md](architecture.md): context, containers, components, a live update end to end |
| Work on the .NET code | [backend-guide.md](backend-guide.md): layout, startup, data access rules, real time, recipes |
| Work on the Angular code | [frontend-guide.md](frontend-guide.md): structure, state, pages, styling, recipes |
| Understand the business data | [domain-model.md](domain-model.md): entities, terms (LTV, PD, LGD), synthetic data assumptions |
| Know how quality is proven | [testing.md](testing.md): four test levels (236 tests), coverage by endpoint, evidence per run |
| Know why something was built this way | [adr/](adr/README.md): eight architecture decision records |
| Call the API | `http://localhost:5080/swagger` when the backend runs (every endpoint, try-it-out) |
| Run the project | [../README.md](../README.md) |

Suggested reading order for a new developer: README → architecture → domain model → the guide
for your side → the decision records.
