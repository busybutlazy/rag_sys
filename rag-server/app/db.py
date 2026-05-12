import os
from arango import ArangoClient

_db = None


def get_db():
    global _db
    if _db is None:
        client = ArangoClient(hosts=os.environ["ARANGO_URL"])
        _db = client.db(
            os.environ["ARANGO_DB"],
            username=os.environ["ARANGO_USER"],
            password=os.environ["ARANGO_PASSWORD"],
        )
    return _db
