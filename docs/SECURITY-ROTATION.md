# Secret rotation checklist

Git history (commits `c2efce9` and `cac010a`) contains a MongoDB Atlas connection
string and a Postgres `Password=` connection string. Rotating the passwords is what
fixes this; scrubbing history alone does not.

## 1. Rotate now
- [ ] **Postgres (Render):** rotate the database password, then update the connection string everywhere it is used.
- [ ] **MongoDB Atlas:** change the database user's password (or create a new user and delete the old one).

## 2. Production secrets live only in Render environment variables
Set these on the Render service (double underscore = `:` in .NET config):

- [ ] `ConnectionStrings__Postgres`
- [ ] `ConnectionStrings__MongoDB`
- [ ] `Jwt__Secret` (this codebase reads `Jwt:Secret`, not `Jwt:Key`)
- [ ] `Email__Smtp__Password`
- [ ] `Paystack__SecretKey` (new; live key in production, test key everywhere else)

No secret, including the Paystack secret key, ever goes in any `appsettings*.json`.

## 3. Local development uses user-secrets
```
cd Hodrac_Backend_MVP2
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5434;Database=hodrac_dev;Username=postgres;Password=..."
dotnet user-secrets set "ConnectionStrings:MongoDB" "mongodb://localhost:27017"
dotnet user-secrets set "Jwt:Secret" "<32+ random chars>"
dotnet user-secrets set "Paystack:SecretKey" "sk_test_..."
```
Prefer pointing local user-secrets at the local Docker databases, not production.

## 4. Optional: purge history
After rotating, the old values are dead, but they can still be removed from history:
```
git filter-repo --replace-text replacements.txt   # list the old secrets, one per line
git push --force --all
```
This rewrites every commit hash from `c2efce9` / `cac010a` onward. Coordinate with
everyone who has a clone (they must re-clone or hard-reset), and check the Render
deploy hooks and any open branches afterwards.
