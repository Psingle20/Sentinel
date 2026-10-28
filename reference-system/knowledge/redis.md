# Shared Redis

The reference environment's Redis allows 200 client connections (`maxclients`). checkout-service and order-service hold small pools; reporting-worker's batch holds many.

When Redis is full, new connections receive "ERR max number of clients reached" and existing connections keep working. The client that holds the most connections is the place to look first.
