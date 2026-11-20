// Placeholder service: replace it with your own, in any language.
// The contract with the bundle is plain HTTP inside the installation network:
//   GET /health       -> 2xx once the service is ready (the agent waits for it)
//   GET /api/records  -> the JSON the bundle's data source reads
// Values of deployment.settings arrive as environment variables, for example
// process.env.UPSTREAM_API_TOKEN. Never log them.
import http from 'node:http';

const port = Number(process.env.PORT ?? 8080);
const records = [
  { id: 'r-1', name: 'First record', amount: 10, updatedAt: '2026-01-01T00:00:00Z' },
  { id: 'r-2', name: 'Second record', amount: 20, updatedAt: '2026-01-02T00:00:00Z' },
];

const send = (res, status, body) => {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(body));
};

http
  .createServer((req, res) => {
    if (req.method !== 'GET') return send(res, 405, { error: 'method not allowed' });
    if (req.url === '/health') return send(res, 200, { status: 'ok' });
    if (req.url === '/api/records') return send(res, 200, records);
    return send(res, 404, { error: 'not found' });
  })
  .listen(port, () => console.log(`listening on ${port}`));
