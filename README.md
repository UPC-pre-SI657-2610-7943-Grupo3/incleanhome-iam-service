# InCleanHome IAM Service

> Identity & Access Management microservice.

Owns the `User` and `WorkerDocument` aggregates. Handles:
- Login via **Auth0** (Universal Login + JWT exchange).
- JWT issuance for the rest of the platform.
- Admin operations: verify users, approve/reject worker documents, suspend accounts.
- FCM device token storage.

## Endpoints

### Public (no JWT)
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/auth/auth0/status` | Is Auth0 enabled? |
| POST | `/api/auth/auth0/login` | Exchange Auth0 token for our JWT |
| POST | `/api/auth/auth0/complete-registration` | Create User + create Profile in one shot (calls Profile Service) |

### Authenticated
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/auth/me` | Get current user (with name/phone from Profile) |
| POST | `/api/auth/worker/upload-document` | Worker uploads PDF |
| POST | `/api/auth/device-token` | Register FCM token |

### Admin only
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/admin/users` | List all users |
| PATCH | `/api/admin/users/{id}/verify` | Verify a user |
| PATCH | `/api/admin/users/{id}/approve-documents` | Approve worker docs (publishes `WorkerDocumentsApprovedEvent`) |
| PATCH | `/api/admin/users/{id}/reject-documents` | Reject worker docs (publishes `WorkerDocumentsRejectedEvent`) |
| PATCH | `/api/admin/users/{id}/suspend` | Suspend user (publishes `UserSuspendedEvent`) |
| PATCH | `/api/admin/users/{id}/clear-suspension` | Lift suspension (publishes `UserSuspensionClearedEvent`) |
| GET | `/api/admin/users/{id}/documents` | Get worker docs |
| DELETE | `/api/admin/users/{id}` | Delete user (publishes `UserDeletedEvent`) |

## Events published

To exchange `incleanhome.iam.events` on the broker:

- `UserRegistered` (on /complete-registration success)
- `WorkerDocumentsApproved`
- `WorkerDocumentsRejected`
- `UserSuspended`
- `UserSuspensionCleared`
- `UserDeleted`

If `RABBITMQ_URL` is the placeholder, events are silently dropped (no crashes).

## External dependencies

- **Auth0** — RS256 token validation + /userinfo
- **Profile Service** (HTTP) — to resolve name/phone and to create profiles on registration
- **CloudAMQP** (RabbitMQ) — event publishing

## Environment variables

| Variable | Required | Purpose |
|---|---|---|
| `JWT_SIGNING_KEY` | YES | At least 32 chars; same key the gateway and other services validate |
| `IAM_DB_CONNECTION` | YES | PostgreSQL connection string |
| `CONSUL_HTTP_ADDR` | no | Default `http://consul:8500` |
| `RABBITMQ_URL` | no | Format `amqps://user:pass@host/vhost`. Placeholder = no broker |
| `AUTH0_CLIENT_SECRET` | only if Auth0 enabled | Auth0 application secret |
| `ADMIN_EMAIL` / `ADMIN_PASSWORD` | no | If both set, an admin user is seeded |

## Run

This service runs as part of the platform:
```bash
cd ../incleanhome-platform
docker compose up --build -d iam-service
```

Direct access (bypassing gateway): http://localhost:5001
Swagger UI (with "Authorize" button): http://localhost:5001/swagger

## Architecture notes

- **Database-per-service**: owns `iam_db`. No other service reads from it directly.
- **JWT validation**: signs HS256 tokens. Validated by gateway + by this service's
  custom `RequestAuthorizationMiddleware`.
- **HTTP coupling to Profile**: `/auth/me` and Auth0 endpoints make HTTP calls to
  Profile Service to keep the frontend contract identical to the monolith. This
  is intentional acoplamiento sincrónico for those endpoints; everything else
  is event-driven.
- **Events**: published with MassTransit + RabbitMQ. Soft-fail if broker is
  unavailable (the state change is already persisted; eventing is best-effort).
