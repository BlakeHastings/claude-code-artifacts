# LiteLLM Admin Operations

## Admin UI

Access at `http://<host>:4000/ui`. Login with username `admin` and password = `LITELLM_MASTER_KEY`.

### Virtual Keys

Create scoped API keys for individual apps/services:
1. Go to **Keys** tab
2. Click **Create New Key**
3. Set name, select accessible models, set optional spend limits
4. Copy the generated `sk-...` key

Virtual keys enable per-app spend tracking, rate limiting, and model access control.

### Models Tab

Shows all configured models from two sources:
- **Config file models** — read-only, loaded at startup
- **DB models** — added via UI or API at runtime, fully editable (requires `STORE_MODEL_IN_DB=True`)

## API Operations

### List Available Models
```bash
curl http://<host>:4000/v1/models \
  -H "Authorization: Bearer <master-key-or-virtual-key>"
```

### Add a Model at Runtime
```bash
curl -X POST http://<host>:4000/model/new \
  -H "Authorization: Bearer <master-key>" \
  -H "Content-Type: application/json" \
  -d '{
    "model_name": "local/codellama",
    "litellm_params": {
      "model": "openai/CodeLlama-13B",
      "api_base": "http://host.docker.internal:1234/v1",
      "api_key": "lm-studio"
    }
  }'
```

Runtime additions are persisted to the DB if `STORE_MODEL_IN_DB=True`.

### Delete a Model
```bash
curl -X POST http://<host>:4000/model/delete \
  -H "Authorization: Bearer <master-key>" \
  -H "Content-Type: application/json" \
  -d '{"id": "<model-id>"}'
```

### Health Check
```bash
curl http://<host>:4000/health
```

### View Spend
```bash
curl http://<host>:4000/spend/logs \
  -H "Authorization: Bearer <master-key>"
```

## Container Operations

### View Logs
```bash
docker logs litellm --tail 50
```

### Restart (reload config)
```bash
docker restart litellm
```

LiteLLM reads the config file at startup only. Bind-mounted config changes require a container restart.

### Verify Config Inside Container
```bash
docker exec litellm cat /app/config.yaml
docker exec litellm grep "forward_client_headers" /app/config.yaml
```

## Common Admin Tasks

### Rotating the Master Key
1. Generate new key
2. Update `LITELLM_MASTER_KEY` in the `.env` file on the host
3. Update the GitHub secret `LITELLM_MASTER_KEY`
4. Restart the container
5. Update any clients using the old master key

### Rotating a Virtual Key
1. Create a new key in the UI
2. Update the client(s) using the old key
3. Delete the old key in the UI
