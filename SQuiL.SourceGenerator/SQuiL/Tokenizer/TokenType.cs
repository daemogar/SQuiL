﻿namespace SQuiL.Tokenizer;

public enum TokenType
{
	END = 0,
	VARIABLE = 1,
	IDENTIFIER = 2,
	BODY = 3,
	INSERT_INTO_TABLE = 4,
	SELECT_VARIABLE = 5,
	// Keywords
	KEYWORD = 1000,
	KEYWORD_DECLARE = KEYWORD + 1,
	KEYWORD_USE = KEYWORD + 2,
	KEYWORD_AS = KEYWORD + 11,
	KEYWORD_IGNORED = KEYWORD + 100,
	KEYWORD_INSERT = KEYWORD_IGNORED + 1,
	KEYWORD_INTO = KEYWORD_IGNORED + 2,
	KEYWORD_VALUES = KEYWORD_IGNORED + 3,
	KEYWORD_SET = KEYWORD_IGNORED + 4,
	// Literals
	LITERAL = 2000,
	LITERAL_NULL = LITERAL + 1,
	LITERAL_STRING = LITERAL + 11,
	LITERAL_NUMBER = LITERAL + 12,
	// Types
	// https://learn.microsoft.com/en-us/sql/t-sql/data-types/data-types-transact-sql?view=sql-server-ver16
	TYPE = 3000,
	TYPE_BOOLEAN = TYPE + 1,
	TYPE_INT = TYPE + 2,
	TYPE_DECIMAL = TYPE + 3,
	TYPE_STRING = TYPE + 4,
	TYPE_DATE = TYPE + 5,
	TYPE_TIME = TYPE + 6,
	TYPE_DATETIME = TYPE + 7,
	TYPE_GUID = TYPE + 8,
	TYPE_TABLE = TYPE + 11,
	TYPE_OBJECT = TYPE + 12,
	//TYPE_RETURN = TYPE + 13,
	//TYPE_RETURNSS = TYPE + 14,
	//TYPE_RETURNS = TYPE + 12,
	TYPE_FUNCTIONS = TYPE + 21,
	TYPE_IDENTITY = TYPE + 101,
	TYPE_DEFAULT = TYPE + 102,
	// Symbols
	SYMBOL = 4000,
	SYMBOL_EQUAL = SYMBOL + '=',
	SYMBOL_COMMA = SYMBOL + ',',
	SYMBOL_LPREN = SYMBOL + '(',
	SYMBOL_RPREN = SYMBOL + ')',
	// Comments
	COMMENT = 5000,
	COMMENT_SINGLELINE = COMMENT + 1,
	COMMENT_MULTILINE = COMMENT + 2
};
