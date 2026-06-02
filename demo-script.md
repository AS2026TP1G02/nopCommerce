./demo/sql-order-trace.sh 1000

curl -X POST http://localhost:8081/mode/unavailable

./demo/sql-order-trace.sh 1000

curl -X POST http://localhost:8081/mode/normal

curl -X POST http://localhost:8081/mode/unavailable

ORDER_TARGET=15 BASE_URL=http://localhost:8080 ./load-test/run-load-test.sh automated

curl -X POST http://localhost:8081/mode/normal
