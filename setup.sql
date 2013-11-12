CREATE TABLE IF NOT EXISTS bigtable(id int NOT NULL AUTO_INCREMENT, date varchar(255), min int(11), PRIMARY KEY (id));
CREATE TABLE IF NOT EXISTS daytable(id int NOT NULL AUTO_INCREMENT, time varchar(255), min int(11), PRIMARY KEY (id));
CREATE TABLE IF NOT EXISTS seedertable(id int NOT NULL AUTO_INCREMENT, date varchar(255), user varchar(255), min int(11), PRIMARY KEY (id));
CREATE TABLE IF NOT EXISTS admintable(id int NOT NULL AUTO_INCREMENT, date varchar(255), user varchar(255), min int(11), PRIMARY KEY (id));
DROP TRIGGER IF EXISTS delete_daytable;
DELIMITER $$
CREATE TRIGGER delete_daytable
	AFTER INSERT ON bigtable FOR EACH ROW
	BEGIN
		DELETE FROM daytable;
	END;
$$
DELIMITER ;