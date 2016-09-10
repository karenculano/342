#! /usr/bin/python
#

import types
import os
import sys
import json
import plistlib
import argparse
from xml.parsers import expat

resources = dict()
stack = list()
inc_list = list()

stack.append(resources)


def end_element_handler(tag):
    if tag == "section":
        stack.pop()


def start_element_handler(tag, attr):
    current = stack[-1]
    key = None
    val = None
    if tag == "section":
        key = attr["name"]
        val = dict()
        stack.append(val)
    elif tag == "integer":
        key = attr["name"]
        val = int(attr["value"])
    elif tag == "json":
        fbase = attr["file"]
        fname = find_file(fbase)
        key = attr["name"]
        if fname is not None and key is not None:
            try:
                with open(fname) as fp:
                    val = json.load(fp)
            except Exception, err:
                print >> sys.stderr, ("Error: %s" % str(err))
        elif fname is None:
            print >> sys.stderr, ("Error: No such json file %s" % fbase)
            sys.exit(1)
    elif tag == "plist":
        fbase = attr["file"]
        fname = find_file(fbase)
        key = attr["name"]
        if fname is not None and key is not None:
            val = plistlib.readPlist(fname)
        elif fname is None:
            print >> sys.stderr, ("Error: No such plist file %s" % fbase)
            sys.exit(1)
    elif tag == "text":
        fbase = attr["file"]
        fname = find_file(fbase)
        key = attr["name"]
        if fname is not None and key is not None:
            try:
                with open(fname) as fp:
                    val = fp.read()
            except Exception, err:
                print >> sys.stderr, ("Error: %s" % str(err))
                sys.exit(1)
        elif fname is None:
            print >> sys.stderr, ("Error: No such string file %s" % fbase)
            sys.exit(1)
    elif tag == "string":
        key = attr["name"]
        val = attr["value"]

    if val is not None:
        if isinstance(current, types.DictType):
            current[key] = val
        elif isinstance(current, types.TupleType):
            current.append(val)


def cdata_handler(s):
    return


def resource_parse_file(infile):
    parser = expat.ParserCreate()
    parser.StartElementHandler = start_element_handler
    parser.EndElementHandler = end_element_handler
    parser.CharacterDataHandler = cdata_handler
    parser.ParseFile(infile)


def find_file(name):
    for inc_dir in inc_list:
        inc = "%s/%s" % (inc_dir, name)
        if os.path.isfile(inc):
            return inc

    if os.path.isfile(name):
        return name

    return None


def main():
    parser = argparse.ArgumentParser(description='Creates a resource json from a resource list')
    parser.add_argument('-I', metavar='<inc path>', help='Include path to search for files')
    parser.add_argument('infile', metavar='<resource list>', type=argparse.FileType('r'), help='Input resources file')
    parser.add_argument('outfile', metavar='<resource json>', type=argparse.FileType('w'), nargs='?',
                        default=sys.stdout, help='Output resources json file [stdout]')
    args = parser.parse_args()

    if args.I:
        inc_list.append(args.I)

    resource_parse_file(args.infile)
    json.dump(resources, args.outfile, indent=4, sort_keys=True)


main()
