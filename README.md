# InCleanHome IAM Service
> Identity and Access Management microservice for InCleanHome.

This service owns the `User` and `WorkerDocument` aggregates. It is responsible for:

- Registering users (local password + Auth0).
- Issuing and validating JWT tokens for the rest of the platform.
- Administrative operations: verifying users, approving/rejecting worker documents,
  suspending accounts.
- Storing FCM device tokens.

This service is part of the larger [InCleanHome platform](https://github.com/UPC-pre-SI657-2610-7943-Grupo3/incleanhome-platform).

## Architecture in one paragraph
Standard DDD layering: Domain (aggregates, value objects, commands, queries,
repository contracts) → Application (command/query services) → Infrastructure
(EF Core persistence, BCrypt hashing, JWT generation, Auth0 client, custom
auth middleware) → Interfaces (REST controllers + DTOs). The service reads its
non-sensitive config (JWT issuer/audience, Auth0 settings, CORS origins, admin
seed email) from **Consul KV** at startup and falls back to `appsettings.json`
if Consul is unreachable. Secrets (database password, JWT signing key, Auth0
client secret) come from environment variables.

## Folder layout (Clean Architecture)
```
src/InCleanHome.IamService/
├── Program.cs                              # composition root
├── appsettings.json                        # fallback config
│
├── Configuration/
│   └── ConsulConfigurationLoader.cs        # loads config/iam-service from KV
│
├── Discovery/
│   ├── ConsulServiceRegistration.cs        # HTTP client for register/deregister
│   └── ConsulRegistrationHostedService.cs  # lifecycle
│
├── Domain/
│   ├── Model/
│   │   ├── Aggregates/    (User, WorkerDocument)
│   │   ├── ValueObjects/  (UserRole, DocumentType)
│   │   ├── Commands/      (Register, Login, Verify, Suspend, ...)
│   │   └── Queries/       (GetUserById, GetUserByEmail, ...)
│   ├── Repositories/      (IUserRepository, IWorkerDocumentRepository, IUnitOfWork)
│   └── Services/          (IUserCommandService, IUserQueryService)
│
├── Application/
│   └── Internal/
│       ├── CommandServices/UserCommandService.cs
│       ├── QueryServices/UserQueryService.cs
│       └── OutboundServices/             (IHashingService, ITokenService)
│
├── Infrastructure/
│   ├── Persistence/
│   │   ├── IamDbContext.cs               # EF Core context (snake_case naming)
│   │   ├── BaseRepository.cs
│   │   ├── Repositories/
│   │   │   ├── UserRepository.cs
│   │   │   └── WorkerDocumentRepository.cs
│   │   └── Extensions/ModelBuilderExtensions.cs
│   ├── Hashing/HashingService.cs         # BCrypt
│   ├── Tokens/                            # JWT issuance & validation
│   ├── ExternalServices/Auth0/           # JWKS validation + /userinfo
│   ├── Pipeline/                          # custom JWT middleware
│   └── Seeding/AdminSeeder.cs            # seeds the admin user on startup
│
└── Interfaces/
    └── REST/
        ├── Controllers/
        │   ├── AuthenticationController.cs   # register, login, me, upload-doc, device-token
        │   ├── Auth0LoginController.cs       # status, login, complete-registration
        │   └── AdminController.cs            # admin user management
        └── Transform/UserPayloadAssembler.cs
```


The IAM Service runs on port `5001` internally. From outside the Docker network,
the only way to reach it is through the API Gateway at `http://localhost:8080`.

## API endpoints
All paths assume the gateway prefix is stripped. From the frontend's perspective:

| Method | Path via gateway | Purpose | Auth |
|---|---|---|---|
| POST | `/api/v1/auth/register` | Local password registration | none |
| POST | `/api/v1/auth/login` | Local password login | none |
| GET  | `/api/v1/auth/me` | Returns current authenticated user | Bearer JWT |
| POST | `/api/v1/auth/worker/upload-document` | Worker uploads PDF | Bearer JWT (worker) |
| POST | `/api/v1/auth/device-token` | Register FCM token | Bearer JWT |
| GET  | `/api/v1/auth/auth0/status` | Is Auth0 enabled? | none |
| POST | `/api/v1/auth/auth0/login` | Exchange Auth0 token for internal JWT | none |
| POST | `/api/v1/auth/auth0/complete-registration` | Create local user from Auth0 token | none |
| GET  | `/api/v1/admin/users` | List all users | Bearer JWT (admin) |
| PATCH | `/api/v1/admin/users/{id}/verify` | Verify a user | Bearer JWT (admin) |
| PATCH | `/api/v1/admin/users/{id}/approve-documents` | Approve worker docs | Bearer JWT (admin) |
| PATCH | `/api/v1/admin/users/{id}/reject-documents` | Reject worker docs | Bearer JWT (admin) |
| PATCH | `/api/v1/admin/users/{id}/suspend` | Suspend user | Bearer JWT (admin) |
| PATCH | `/api/v1/admin/users/{id}/clear-suspension` | Lift suspension | Bearer JWT (admin) |
| GET  | `/api/v1/admin/users/{id}/documents` | Get worker docs | Bearer JWT (admin) |
| DELETE | `/api/v1/admin/users/{id}` | Delete user | Bearer JWT (admin) |

Swagger UI is available at `http://localhost:5001/swagger` (only when accessed
directly, not through the gateway, since the gateway does not currently expose
Swagger).

## Database
This service owns a single PostgreSQL database (`iam_db` in docker-compose).
Tables (snake_case):

- `users` — User aggregate
- `worker_documents` — uploaded PDFs (stored as base64)

The schema is created automatically on first startup via
`Database.EnsureCreatedAsync()`. 

## Architecture decisions

### Database-per-service
This service owns its own `iam_db`. No other microservice talks to this database
directly. If another service needs IAM data, it goes through the IAM REST API.

### JWT validated by both Gateway and microservice
The gateway validates the JWT before routing (defense at the edge), and this
service also validates the JWT in its own middleware (defense in depth). The
custom `RequestAuthorizationMiddleware` not only validates the signature but
also fetches the full `User` aggregate from the database and stores it in
`HttpContext.Items["User"]` so controllers can use it directly.

### Auth0 complete-registration changed
In the monolith, `POST /api/auth/auth0/complete-registration` created BOTH the
User AND the Profile (Client or Worker) in a single request because Profile
lived in the same process. In this microservice split, IAM only creates the
User aggregate. The endpoint returns `needsProfileSetup: true` in the response
so the frontend knows to call Profile Service afterwards.

**Frontend change required**: after a successful `complete-registration`, the
frontend must `POST /api/v1/profiles/...` with the profile data.

### Admin notifications postponed
Endpoints that previously sent push notifications via the Notifications module
(approve-documents, reject-documents, suspend, clear-suspension) no longer send
those notifications. When the Communication Service is added with a RabbitMQ
broker, these endpoints will publish events (`WorkerDocumentsApproved`,
`WorkerDocumentsRejected`, `UserSuspended`, `UserSuspensionCleared`) that
Communication Service will consume to send the notifications. For this iteration
the notifications are silently skipped (with TODO comments in the code).

## License

For academic use - InCleanHome team.
