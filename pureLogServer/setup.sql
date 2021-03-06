/* 

Don't forget to select your default schema!

The 'daytable' stores a row every minute including the current player count.
The 'bigtable' stores the daily totals of the entries in the daytable. 
Each game server should have a unique daytable and bigtable.

*/

CREATE TABLE IF NOT EXISTS daytable(id int NOT NULL AUTO_INCREMENT, time varchar(255), min int(11), PRIMARY KEY (id));
CREATE TABLE IF NOT EXISTS bigtable(id int NOT NULL AUTO_INCREMENT, date varchar(255), min int(11), emptyTime int(11), PRIMARY KEY (id));


/* 

The 'ipptable', or 'Individual Player Playtime Table' stores weekly data for individual players.
Multiple servers can point to the same 'ipptable'.
Each new week starts on midnight Sunday.

*/

CREATE TABLE IF NOT EXISTS ipptable (name VARCHAR(128), guid VARCHAR(128), weekOf DATE, time INT, eventKey VARCHAR(128), primary key (guid, weekOf, eventKey));
