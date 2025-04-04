```bash
dotnet workload install wasi-experimental
```

Pull Docker Image
```bash
docker run --rm --pull always -p 3000:3000 clockworklabs/spacetime start
```

Set your SpacetimeDB server to point to your local instance:

```bash
 spacetime server add localhost:3000 --url http://localhost:3000 --no-fingerprint
```

Build Module
```bash
spacetime build
```

Publish Module to local instance
```bash
spacetime publish --project-path  ~/RiderProjects/Kulicha/server kulicha --server local
```

```bash
spacetime publish --server local kulicha
```

Start Server

```bash
spacetime start
```

Kill Process

```bash
pkill spacetimedb
```

Clear DB
```bash
spacetime server clear
``` 